using Application.Abstractions.Authentication;
using Application.PfaRegistrations.GetAll;
using Application.PfaRegistrations.Onboarding.Eligibility;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>
/// Clientul e în înrolare din clipa în care și-a făcut contul, nu de la pasul 2, unde se creează
/// dosarul PFA.
///
/// Lista „În curs de înrolare” pornea de la dosare, iar validarea eligibilității cerea un dosar.
/// Cum pasul 2 se deschide abia pe validarea eligibilității, un client cu actele încărcate nu
/// apărea în admin și nici nu mai putea trece mai departe.
/// </summary>
public sealed class AccountsWithoutRegistrationTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid ContabilId = Guid.NewGuid();

    [Fact]
    public async Task A_client_without_a_registration_is_listed_by_account()
    {
        using ApplicationDbContext db = NewDb();
        User client = AddUser(db, UserRole.Client, "sofer@example.test");
        await db.SaveChangesAsync(CancellationToken.None);

        PfaRegistrationSummary row = (await List(db, AdminId)).Items.ShouldHaveSingleItem();

        row.HasRegistration.ShouldBeFalse();
        row.Id.ShouldBe(client.Id);
        row.UserId.ShouldBe(client.Id);
        row.AccountStatus.ShouldBe("Cont nou");
        row.AwaitingAdminAction.ShouldBeFalse();
    }

    [Fact]
    public async Task Uploaded_eligibility_documents_put_the_ball_in_the_admins_court()
    {
        using ApplicationDbContext db = NewDb();
        User client = AddUser(db, UserRole.Client, "sofer@example.test");
        AddDocument(db, client.Id, DateTime.UtcNow);
        await db.SaveChangesAsync(CancellationToken.None);

        PfaRegistrationSummary row = (await List(db, AdminId)).Items.ShouldHaveSingleItem();

        row.AwaitingAdminAction.ShouldBeTrue();
        row.DocumentCount.ShouldBe(1);
    }

    [Fact]
    public async Task Other_roles_and_clients_with_a_registration_are_not_duplicated()
    {
        using ApplicationDbContext db = NewDb();
        AddUser(db, UserRole.CarPoster, "srl@example.test");
        User withRegistration = AddUser(db, UserRole.Client, "pfa@example.test");
        db.PfaRegistrations.Add(new PfaRegistration
        {
            Id = Guid.NewGuid(),
            UserId = withRegistration.Id,
            RegistrationType = RegistrationType.AmPfa,
        });
        await db.SaveChangesAsync(CancellationToken.None);

        PfaRegistrationSummary row = (await List(db, AdminId)).Items.ShouldHaveSingleItem();

        row.HasRegistration.ShouldBeTrue();
        row.UserId.ShouldBe(withRegistration.Id);
    }

    [Fact]
    public async Task A_contabil_does_not_see_accounts_without_a_registration()
    {
        using ApplicationDbContext db = NewDb();
        AddUser(db, UserRole.Client, "sofer@example.test");
        await db.SaveChangesAsync(CancellationToken.None);

        (await List(db, ContabilId)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Eligibility_can_be_validated_by_account_before_the_registration_exists()
    {
        using ApplicationDbContext db = NewDb();
        User client = AddUser(db, UserRole.Client, "sofer@example.test");
        AddDocument(db, client.Id, DateTime.UtcNow);
        await db.SaveChangesAsync(CancellationToken.None);

        Result result = await new ReviewEligibilityCommandHandler(db).Handle(
            new ReviewEligibilityCommand(client.Id, AdminId, Approve: true, Note: null),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        OnboardingEligibilityProfile profile = await db.OnboardingEligibilityProfiles.SingleAsync(p => p.UserId == client.Id);
        profile.AdminValidatedAtUtc.ShouldNotBeNull();

        // Validat, deci nu mai așteaptă nimic de la admin.
        (await List(db, AdminId)).Items.ShouldHaveSingleItem().AwaitingAdminAction.ShouldBeFalse();
    }

    [Fact]
    public async Task An_unknown_id_is_still_not_found()
    {
        using ApplicationDbContext db = NewDb();
        AddUser(db, UserRole.CarPoster, "srl@example.test");
        await db.SaveChangesAsync(CancellationToken.None);

        Result result = await new ReviewEligibilityCommandHandler(db).Handle(
            new ReviewEligibilityCommand(Guid.NewGuid(), AdminId, Approve: true, Note: null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }

    private static async Task<PfaRegistrationListResponse> List(ApplicationDbContext db, Guid callerId)
    {
        Result<PfaRegistrationListResponse> result = await new GetAllPfaRegistrationsQueryHandler(db, new Caller(callerId))
            .Handle(new GetAllPfaRegistrationsQuery(1, 100), CancellationToken.None);
        return result.Value;
    }

    private static ApplicationDbContext NewDb()
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new Events());

        db.Users.Add(new User { Id = AdminId, Email = "admin@example.test", Role = UserRole.Admin });
        db.Users.Add(new User { Id = ContabilId, Email = "contabil@example.test", Role = UserRole.Contabil });
        return db;
    }

    private static User AddUser(ApplicationDbContext db, UserRole role, string email)
    {
        var user = new User { Id = Guid.NewGuid(), Email = email, FirstName = "Andrei", LastName = "Ionescu", Role = role };
        db.Users.Add(user);
        return user;
    }

    private static void AddDocument(ApplicationDbContext db, Guid userId, DateTime uploadedAtUtc) =>
        db.Documents.Add(new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Category = DocumentCategory.CarteIdentitate,
            Status = DocumentStatus.Pending,
            UploadedAtUtc = uploadedAtUtc,
        });

    private sealed class Caller(Guid userId) : IUserContext
    {
        public Guid UserId { get; } = userId;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
