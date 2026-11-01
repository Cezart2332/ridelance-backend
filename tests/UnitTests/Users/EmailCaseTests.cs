using Application.Abstractions;
using Application.Abstractions.Authentication;
using Application.Users.Login;
using Application.Users.PasswordReset;
using Application.Users.Register;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Users;

/// <summary>„Ion@Example.com” și „ion@example.com” sunt același cont, la înregistrare, login și resetare.</summary>
public sealed class EmailCaseTests
{
    private const string Password = "parola-lunga-1";

    [Fact]
    public void Normalize_trims_and_lowercases() =>
        EmailAddress.Normalize("  Ion.Popescu@Example.COM ").ShouldBe("ion.popescu@example.com");

    [Fact]
    public async Task Registration_stores_the_address_in_lowercase_and_rejects_the_same_address_in_another_case()
    {
        await using ApplicationDbContext db = NewDb();

        (await Register(db, " Ion@Example.com")).IsSuccess.ShouldBeTrue();
        (await db.Users.SingleAsync()).Email.ShouldBe("ion@example.com");

        (await Register(db, "ION@EXAMPLE.COM")).Error.ShouldBe(UserErrors.EmailNotUnique);
        (await db.Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Login_works_whatever_the_case()
    {
        await using ApplicationDbContext db = NewDb();
        await Register(db, "ion@example.com");

        Result<LoginResponse> result = await new LoginUserCommandHandler(db, new Hasher(), new Tokens(), new Clock())
            .Handle(new LoginUserCommand("  ION@Example.com ", Password), default);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Password_reset_finds_the_account_whatever_the_case()
    {
        await using ApplicationDbContext db = NewDb();
        await Register(db, "ion@example.com");
        var mail = new Mail();

        (await new RequestPasswordResetCommandHandler(db, mail, new Renderer())
            .Handle(new RequestPasswordResetCommand("Ion@EXAMPLE.com"), default)).IsSuccess.ShouldBeTrue();

        mail.To.ShouldBe(["ion@example.com"]);
        (await db.Users.SingleAsync()).PasswordResetCodeHash.ShouldNotBeNull();
    }

    private static Task<Result<Guid>> Register(ApplicationDbContext db, string email) =>
        new RegisterUserCommandHandler(db, new Hasher()).Handle(new RegisterUserCommand(email, Password), default);

    private static ApplicationDbContext NewDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private sealed class Hasher : IPasswordHasher
    {
        public string Hash(string password) => $"hash:{password}";

        public bool Verify(string password, string passwordHash) => passwordHash == $"hash:{password}";
    }

    private sealed class Tokens : ITokenProvider
    {
        public string Create(User user) => $"access-{user.Id}";

        public string CreateRefreshToken() => Guid.NewGuid().ToString("N");
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private sealed class Renderer : IMjmlRenderer
    {
        public string Render(string mjml) => mjml;
    }

    private sealed class Mail : IEmailService
    {
        public List<string> To { get; } = [];

        public Task<Result> SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            To.Add(to);
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
