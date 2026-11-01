using Application.Abstractions;
using Application.Abstractions.Authentication;
using Application.Referrals;
using Application.Users.Register;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Referrals;

/// <summary>Codul de recomandare, invitațiile pe email și stadiul fiecărui prieten invitat.</summary>
public sealed class ReferralTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://app.test/" })
        .Build();

    [Fact]
    public async Task The_code_is_generated_once_and_the_link_carries_it()
    {
        await using ApplicationDbContext db = NewDb();
        Guid client = AddClient(db, "ion@example.com");
        await db.SaveChangesAsync();

        ReferralsResponse first = (await Mine(db, client)).Value;
        ReferralsResponse second = (await Mine(db, client)).Value;

        first.Code.Length.ShouldBe(ReferralCodes.Length);
        second.Code.ShouldBe(first.Code);
        first.Link.ShouldBe($"https://app.test/inregistrare?ref={first.Code}");
        first.Referrals.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_invitation_is_sent_and_shows_as_not_accepted()
    {
        await using ApplicationDbContext db = NewDb();
        Guid client = AddClient(db, "ion@example.com");
        await db.SaveChangesAsync();
        var mail = new Mail();

        Result<ReferralsResponse> result = await Invite(db, client, mail).Handle(new InviteReferralCommand("  Maria@Example.com "), default);

        result.IsSuccess.ShouldBeTrue();
        mail.To.ShouldBe(["maria@example.com"]);
        mail.Bodies.Single().ShouldContain(result.Value.Link);
        ReferralDto row = result.Value.Referrals.ShouldHaveSingleItem();
        row.Email.ShouldBe("maria@example.com");
        row.Accepted.ShouldBeFalse();
        row.Stage.ShouldBe("Invited");

        // A doua invitație în aceeași zi nu mai pleacă.
        (await Invite(db, client, mail).Handle(new InviteReferralCommand("maria@example.com"), default))
            .Error.ShouldBe(ReferralErrors.SentRecently);
    }

    [Fact]
    public async Task Existing_accounts_and_the_own_address_cannot_be_invited()
    {
        await using ApplicationDbContext db = NewDb();
        Guid client = AddClient(db, "ion@example.com");
        AddClient(db, "maria@example.com");
        await db.SaveChangesAsync();

        (await Invite(db, client, new Mail()).Handle(new InviteReferralCommand("maria@example.com"), default))
            .Error.ShouldBe(ReferralErrors.AlreadyRegistered);
        (await Invite(db, client, new Mail()).Handle(new InviteReferralCommand("ION@example.com"), default))
            .Error.ShouldBe(ReferralErrors.OwnEmail);
        (await Invite(db, client, new Mail()).Handle(new InviteReferralCommand("nu-e-email"), default))
            .Error.ShouldBe(ReferralErrors.InvalidEmail);
    }

    [Fact]
    public async Task Registering_with_the_code_links_the_account_and_shows_its_stage()
    {
        await using ApplicationDbContext db = NewDb();
        Guid client = AddClient(db, "ion@example.com");
        await db.SaveChangesAsync();
        string code = (await Mine(db, client)).Value.Code;
        await Invite(db, client, new Mail()).Handle(new InviteReferralCommand("maria@example.com"), default);

        Guid maria = (await new RegisterUserCommandHandler(db, new FakeHasher())
            .Handle(new RegisterUserCommand("maria@example.com", "parola-lunga", ReferralCode: $" {code} "), default)).Value;
        Guid andrei = (await new RegisterUserCommandHandler(db, new FakeHasher())
            .Handle(new RegisterUserCommand("andrei@example.com", "parola-lunga", ReferralCode: code), default)).Value;

        (await db.Users.SingleAsync(u => u.Id == maria)).ReferredByUserId.ShouldBe(client);
        (await db.Users.SingleAsync(u => u.Id == andrei)).ReferralCode.ShouldNotBeNullOrEmpty();

        db.UserSubscriptions.Add(new UserSubscription { Id = Guid.NewGuid(), UserId = andrei, Plan = SubscriptionPlan.PfaFull, Status = SubscriptionStatus.Active });
        await db.SaveChangesAsync();

        IReadOnlyList<ReferralDto> rows = (await Mine(db, client)).Value.Referrals;
        rows.Count.ShouldBe(2);
        rows.Single(r => r.Email == "maria@example.com").ShouldSatisfyAllConditions(
            r => r.Accepted.ShouldBeTrue(),
            r => r.InvitedAtUtc.ShouldNotBeNull(),
            r => r.Stage.ShouldBe("Onboarding"));
        rows.Single(r => r.Email == "andrei@example.com").ShouldSatisfyAllConditions(
            r => r.InvitedAtUtc.ShouldBeNull(),
            r => r.Stage.ShouldBe("Active"));
    }

    [Fact]
    public async Task An_unknown_code_does_not_block_registration()
    {
        await using ApplicationDbContext db = NewDb();

        Result<Guid> result = await new RegisterUserCommandHandler(db, new FakeHasher())
            .Handle(new RegisterUserCommand("maria@example.com", "parola-lunga", ReferralCode: "NUEXISTA"), default);

        result.IsSuccess.ShouldBeTrue();
        (await db.Users.SingleAsync()).ReferredByUserId.ShouldBeNull();
    }

    [Fact]
    public async Task Only_an_admin_sees_another_clients_referrals()
    {
        await using ApplicationDbContext db = NewDb();
        Guid client = AddClient(db, "ion@example.com");
        var admin = new User { Id = Guid.NewGuid(), Email = "admin@ridelance.ro", Role = UserRole.Admin };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        (await new GetClientReferralsQueryHandler(db, new StubUser(admin.Id), Configuration)
            .Handle(new GetClientReferralsQuery(client), default)).IsSuccess.ShouldBeTrue();
        (await new GetClientReferralsQueryHandler(db, new StubUser(client), Configuration)
            .Handle(new GetClientReferralsQuery(client), default)).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(UserRole.Client, null, null, "Onboarding")]
    [InlineData(UserRole.Client, "2026-10-01", null, "Inactive")]
    [InlineData(UserRole.Client, "2026-10-01", SubscriptionStatus.Expired, "Inactive")]
    [InlineData(UserRole.Client, null, SubscriptionStatus.Active, "Active")]
    [InlineData(UserRole.CarPoster, null, null, "Onboarding")]
    [InlineData(UserRole.CarPoster, null, SubscriptionStatus.Cancelled, "Inactive")]
    public void Stage_follows_onboarding_and_subscription(UserRole role, string? completed, SubscriptionStatus? subscription, string stage) =>
        ReferralHistory.Stage(null, role, completed is null ? null : DateTime.Parse(completed, System.Globalization.CultureInfo.InvariantCulture), subscription)
            .ShouldBe(stage);

    private static Task<Result<ReferralsResponse>> Mine(ApplicationDbContext db, Guid user) =>
        new GetMyReferralsQueryHandler(db, new StubUser(user), Configuration).Handle(new GetMyReferralsQuery(), default);

    private static InviteReferralCommandHandler Invite(ApplicationDbContext db, Guid user, Mail mail) =>
        new(db, new StubUser(user), Configuration, mail, new Renderer());

    private static Guid AddClient(ApplicationDbContext db, string email)
    {
        var user = new User { Id = Guid.NewGuid(), Email = email, FirstName = "Ion", LastName = "Popescu", Role = UserRole.Client };
        db.Users.Add(user);
        return user.Id;
    }

    private static ApplicationDbContext NewDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private sealed class StubUser(Guid id) : IUserContext
    {
        public Guid UserId { get; } = id;
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hash:{password}";

        public bool Verify(string password, string passwordHash) => passwordHash == $"hash:{password}";
    }

    private sealed class Renderer : IMjmlRenderer
    {
        public string Render(string mjml) => mjml;
    }

    private sealed class Mail : IEmailService
    {
        public List<string> To { get; } = [];

        public List<string> Bodies { get; } = [];

        public Task<Result> SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            To.Add(to);
            Bodies.Add(htmlBody);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> SendEmailWithAttachmentsAsync(string to, string subject, string htmlBody, IReadOnlyList<EmailAttachmentContent> attachments, bool highPriority = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
