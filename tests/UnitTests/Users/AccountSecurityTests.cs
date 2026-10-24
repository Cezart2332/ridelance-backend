using Application.Abstractions.Authentication;
using Application.Users.AccountVerification;
using Application.Users.PasswordReset;
using Application.Users.TwoFactor;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Users;

/// <summary>Confirmarea contului impusă pe server și resetarea parolei uitate.</summary>
public sealed class AccountSecurityTests
{
    private static readonly DateTime After = AccountVerificationGate.DefaultEnforceFromUtc.AddDays(1);

    [Fact]
    public void NewUnverifiedAccount_IsHeldUntilBothCodesAreConfirmed()
    {
        AccountVerificationGate gate = Gate(email: true, phone: true);
        gate.Pending(UserRole.Client, After, emailVerified: false, phoneVerified: false).ShouldBe(new PendingVerification(true, true));
        gate.Pending(UserRole.Client, After, emailVerified: true, phoneVerified: false).ShouldBe(new PendingVerification(false, true));
        gate.Pending(UserRole.CarPoster, After, emailVerified: true, phoneVerified: true).Any.ShouldBeFalse();
    }

    [Fact]
    public void AccountsFromBeforeTheRule_StaffAndTestSites_AreNotHeld()
    {
        Gate(email: true, phone: true)
            .Pending(UserRole.Client, AccountVerificationGate.DefaultEnforceFromUtc.AddDays(-1), false, false).Any.ShouldBeFalse();
        Gate(email: true, phone: true).Pending(UserRole.Admin, After, false, false).Any.ShouldBeFalse();
        Gate(email: false, phone: false).Pending(UserRole.Client, After, false, false).Any.ShouldBeFalse();
    }

    [Fact]
    public async Task WrongResetCode_CountsAndLeavesThePassword()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db, "123456");

        Result result = await Reset(db).Handle(new ResetPasswordCommand(user.Email, "000000", "parola-noua-1"), default);

        result.IsFailure.ShouldBeTrue();
        user.PasswordResetAttempts.ShouldBe(1);
        user.PasswordHash.ShouldBe("old");
    }

    [Fact]
    public async Task RightResetCode_SetsThePasswordAndEndsSessions()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db, "123456");

        Result result = await Reset(db).Handle(new ResetPasswordCommand(user.Email, "123456", "parola-noua-1"), default);

        result.IsSuccess.ShouldBeTrue();
        user.PasswordHash.ShouldBe("hash:parola-noua-1");
        user.RefreshToken.ShouldBeNull();
        user.PasswordResetCodeHash.ShouldBeNull();
        user.IsEmailVerified.ShouldBeTrue();

        // Codul e de unică folosință.
        (await Reset(db).Handle(new ResetPasswordCommand(user.Email, "123456", "alta-parola-2"), default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task ExpiredResetCode_IsRefused()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db, "123456");
        user.PasswordResetCodeExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        (await Reset(db).Handle(new ResetPasswordCommand(user.Email, "123456", "parola-noua-1"), default))
            .Error.ShouldBe(UserErrors.VerificationCodeExpired);
    }

    private static AccountVerificationGate Gate(bool email, bool phone) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [AccountVerificationGate.RequireEmailKey] = email ? "true" : "false",
            [AccountVerificationGate.RequirePhoneKey] = phone ? "true" : "false",
        }).Build());

    private static ResetPasswordCommandHandler Reset(ApplicationDbContext db) =>
        new ResetPasswordCommandHandler(db, new FakeHasher());

    private static ApplicationDbContext Database() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new NoEvents());

    private static async Task<User> AddUser(ApplicationDbContext db, string code)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "sofer@example.test",
            PasswordHash = "old",
            RefreshToken = "refresh",
            PasswordResetCodeHash = TwoFactorSupport.Hash(code),
            PasswordResetCodeExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => "hash:" + password;

        public bool Verify(string password, string passwordHash) => passwordHash == Hash(password);
    }

    private sealed class NoEvents : Infrastructure.DomainEvents.IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
