using System.Text;
using Application.Abstractions;
using Application.Abstractions.Authentication;
using Application.Users.Impersonate;
using Application.Users.Login;
using Application.Users.RefreshToken;
using Application.Users.Staff;
using Application.Users.TwoFactor;
using Domain.Users;
using Infrastructure.Authentication;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Infrastructure.Security;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Users;

/// <summary>
/// Echipa: primul admin din configurare, invitațiile (doar proprietarul invită admini), 2FA
/// obligatoriu la creare și la fiecare login, codurile de recuperare și blocarea după coduri greșite.
/// </summary>
public sealed class StaffSecurityTests : IDisposable
{
    private const string Password = "Parola-lunga-2026";

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly Clock _clock = new() { UtcNow = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc) };
    private readonly SecretProtector _secrets = new(Options.Create(new EncryptionSettings { Key = "cheie-de-test" }));
    private readonly PasswordHasher _hasher = new();
    private readonly Tokens _tokens = new();
    private readonly Mail _mail = new();
    private readonly IConfiguration _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://app.test" }).Build();

    [Fact]
    public void Totp_matches_the_rfc_6238_test_vector()
    {
        // RFC 6238, anexa B: secretul ASCII „12345678901234567890”, T = 59 s → 94287082 (ultimele 6 cifre).
        string secret = Base32.Encode(Encoding.ASCII.GetBytes("12345678901234567890"));
        Totp.CodeAt(secret, 59 / 30).ShouldBe("287082");
        Base32.Decode(secret).ShouldBe(Encoding.ASCII.GetBytes("12345678901234567890"));
    }

    [Fact]
    public async Task Bootstrap_invites_the_owner_only_while_there_is_no_admin()
    {
        string? link = (await Bootstrap("owner@ridelance.test")).Value;
        link.ShouldNotBeNull();
        link.ShouldStartWith("https://app.test/invitatie/");
        _mail.To.ShouldBe(["owner@ridelance.test"]);

        (await Bootstrap(null)).Value.ShouldBeNull();

        User owner = await AcceptAndEnroll(link[(link.LastIndexOf('/') + 1)..]);
        owner.IsOwner.ShouldBeTrue();
        owner.Role.ShouldBe(UserRole.Admin);

        (await Bootstrap("altcineva@ridelance.test")).Value.ShouldBeNull();
        (await _db.StaffInvitations.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Only_the_owner_invites_admins_while_any_admin_invites_accountants()
    {
        User owner = await Staff(UserRole.Admin, owner: true);
        User admin = await Staff(UserRole.Admin);

        (await Invite(admin, UserRole.Admin)).Error.ShouldBe(StaffErrors.OwnerOnly);
        (await Invite(admin, UserRole.Contabil)).IsSuccess.ShouldBeTrue();
        (await Invite(owner, UserRole.Admin)).IsSuccess.ShouldBeTrue();
        (await Invite(await Staff(UserRole.Contabil), UserRole.Contabil)).Error.ShouldBe(StaffErrors.NotAdmin);
        (await Invite(owner, UserRole.Client)).Error.ShouldBe(StaffErrors.InvalidRole);

        StaffOverviewDto overview = (await new GetStaffQueryHandler(_db, As(admin), _clock).Handle(new GetStaffQuery(), CancellationToken.None)).Value;
        overview.CanInviteAdmins.ShouldBeFalse();
        overview.Invitations.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Invited_account_has_no_session_until_two_factor_is_set_up()
    {
        User owner = await Staff(UserRole.Admin, owner: true);
        string token = await InviteToken(owner, UserRole.Contabil);

        LoginResponse accepted = (await Accept(token, Password)).Value;
        accepted.IsChallenge.ShouldBeTrue();
        accepted.TwoFactor.ShouldBe("SETUP");
        accepted.AccessToken.ShouldBeEmpty();

        // Invitația e de unică folosință.
        (await Accept(token, Password)).Error.ShouldBe(StaffErrors.InvitationInvalid);

        TwoFactorSetupDto setup = (await new StartTwoFactorSetupCommandHandler(_db, _secrets, _clock).Handle(new StartTwoFactorSetupCommand(accepted.ChallengeToken!), CancellationToken.None)).Value;
        setup.QrText.ShouldContain($"secret={setup.Secret}");

        Result<TwoFactorEnabledDto> wrong = await Confirm(accepted.ChallengeToken!, "000000");
        wrong.Error.ShouldBe(TwoFactorErrors.CodeInvalid);

        TwoFactorEnabledDto enabled = (await Confirm(accepted.ChallengeToken!, Totp.CodeAt(setup.Secret, Totp.StepAt(_clock.UtcNow)))).Value;
        enabled.Session.AccessToken.ShouldNotBeEmpty();
        enabled.RecoveryCodes.Count.ShouldBe(10);
        User user = await _db.Users.SingleAsync(u => u.Role == UserRole.Contabil);
        user.IsTwoFactorEnabled.ShouldBeTrue();
        user.TwoFactorSecret.ShouldNotBe(setup.Secret);
    }

    [Fact]
    public async Task Staff_login_needs_a_fresh_code_and_a_used_code_is_rejected()
    {
        (User user, string secret, _) = await EnrolledStaff(UserRole.Contabil);

        LoginResponse challenge = (await Login(user.Email, Password)).Value;
        challenge.TwoFactor.ShouldBe("VERIFY");
        challenge.AccessToken.ShouldBeEmpty();
        (await Login(user.Email, "gresit")).Error.ShouldBe(UserErrors.InvalidCredentials);

        // Codul folosit la configurare (același pas de 30 s) nu mai trece.
        (await Verify(challenge.ChallengeToken!, Totp.CodeAt(secret, Totp.StepAt(_clock.UtcNow)))).Error.ShouldBe(TwoFactorErrors.CodeInvalid);

        _clock.UtcNow = _clock.UtcNow.AddSeconds(30);
        LoginResponse session = (await Verify(challenge.ChallengeToken!, Totp.CodeAt(secret, Totp.StepAt(_clock.UtcNow)))).Value;
        session.AccessToken.ShouldNotBeEmpty();
        session.RefreshToken.ShouldNotBeEmpty();

        // Tokenul de pas s-a consumat.
        (await Verify(challenge.ChallengeToken!, "123456")).Error.ShouldBe(TwoFactorErrors.ChallengeInvalid);
    }

    [Fact]
    public async Task Recovery_code_works_once_and_five_wrong_codes_lock_the_account()
    {
        (User user, _, IReadOnlyList<string> codes) = await EnrolledStaff(UserRole.Admin);

        LoginResponse first = (await Login(user.Email, Password)).Value;
        (await Verify(first.ChallengeToken!, codes[0].ToUpperInvariant())).Value.AccessToken.ShouldNotBeEmpty();

        LoginResponse second = (await Login(user.Email, Password)).Value;
        (await Verify(second.ChallengeToken!, codes[0])).Error.ShouldBe(TwoFactorErrors.CodeInvalid);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Verify(second.ChallengeToken!, "111111");
        }

        (await Verify(second.ChallengeToken!, "111111")).Error.ShouldBe(TwoFactorErrors.LockedOut);
        (await Verify(second.ChallengeToken!, codes[1])).Error.ShouldBe(TwoFactorErrors.LockedOut);

        _clock.UtcNow = _clock.UtcNow.AddMinutes(16);
        LoginResponse third = (await Login(user.Email, Password)).Value;
        (await Verify(third.ChallengeToken!, codes[1])).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Staff_without_two_factor_cannot_refresh_and_cannot_be_impersonated()
    {
        User admin = await Staff(UserRole.Admin, owner: true);
        User accountant = await Staff(UserRole.Contabil);
        accountant.RefreshToken = "vechi";
        accountant.RefreshTokenExpiryUtc = DateTime.UtcNow.AddDays(1);
        var client = new User { Id = Guid.NewGuid(), Email = "client@ridelance.test", Role = UserRole.Client, PasswordHash = "-" };
        _db.Users.Add(client);
        await _db.SaveChangesAsync();

        (await new RefreshTokenCommandHandler(_db, _tokens).Handle(new RefreshTokenCommand("vechi"), CancellationToken.None)).Error.ShouldBe(UserErrors.InvalidRefreshToken);

        var impersonate = new ImpersonateUserCommandHandler(_db, _tokens, As(admin));
        (await impersonate.Handle(new ImpersonateUserCommand(accountant.Id), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await impersonate.Handle(new ImpersonateUserCommand(client.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Admins_reset_accountants_but_only_the_owner_resets_admins()
    {
        User owner = await Staff(UserRole.Admin, owner: true);
        (User admin, _, _) = await EnrolledStaff(UserRole.Admin);
        (User accountant, _, _) = await EnrolledStaff(UserRole.Contabil);

        (await Reset(admin, accountant.Id)).IsSuccess.ShouldBeTrue();
        (await _db.Users.SingleAsync(u => u.Id == accountant.Id)).IsTwoFactorEnabled.ShouldBeFalse();
        (await _db.TwoFactorRecoveryCodes.CountAsync(c => c.UserId == accountant.Id)).ShouldBe(0);

        (await Reset(admin, owner.Id)).Error.ShouldBe(StaffErrors.OwnerOnly);
        (await Reset(admin, admin.Id)).Error.ShouldBe(StaffErrors.CannotResetSelf);
        (await Reset(owner, admin.Id)).IsSuccess.ShouldBeTrue();

        // Fără 2FA, următorul login cere din nou configurarea.
        (await Login(accountant.Email, Password)).Value.TwoFactor.ShouldBe("SETUP");
    }

    public void Dispose() => _db.Dispose();

    // ─── Ajutoare ──────────────────────────────────────────────────────────────────────────────

    private Task<Result<string?>> Bootstrap(string? email) =>
        new EnsureAdminBootstrapCommandHandler(_db, _mail, new Renderer(), _config, _clock).Handle(new EnsureAdminBootstrapCommand(email), CancellationToken.None);

    private Task<Result<StaffInvitationDto>> Invite(User caller, UserRole role) =>
        new InviteStaffCommandHandler(_db, As(caller), _mail, new Renderer(), _config, _clock)
            .Handle(new InviteStaffCommand("Ana Pop", $"{Guid.NewGuid():N}@ridelance.test", role.ToString()), CancellationToken.None);

    private async Task<string> InviteToken(User caller, UserRole role)
    {
        (await Invite(caller, role)).IsSuccess.ShouldBeTrue();
        return _mail.Links[^1][(_mail.Links[^1].LastIndexOf('/') + 1)..];
    }

    private Task<Result<LoginResponse>> Accept(string token, string password) =>
        new AcceptStaffInvitationCommandHandler(_db, _hasher, _clock).Handle(new AcceptStaffInvitationCommand(token, password), CancellationToken.None);

    private Task<Result<TwoFactorEnabledDto>> Confirm(string challenge, string code) =>
        new ConfirmTwoFactorSetupCommandHandler(_db, _secrets, _tokens, _clock).Handle(new ConfirmTwoFactorSetupCommand(challenge, code), CancellationToken.None);

    private Task<Result<LoginResponse>> Verify(string challenge, string code) =>
        new VerifyTwoFactorCommandHandler(_db, _secrets, _tokens, _clock).Handle(new VerifyTwoFactorCommand(challenge, code), CancellationToken.None);

    private Task<Result<LoginResponse>> Login(string email, string password) =>
        new LoginUserCommandHandler(_db, _hasher, _tokens, _clock).Handle(new LoginUserCommand(email, password), CancellationToken.None);

    private Task<Result> Reset(User caller, Guid target) =>
        new ResetStaffTwoFactorCommandHandler(_db, As(caller)).Handle(new ResetStaffTwoFactorCommand(target), CancellationToken.None);

    private async Task<User> Staff(UserRole role, bool owner = false)
    {
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@ridelance.test", FirstName = "Test", Role = role, IsOwner = owner, PasswordHash = _hasher.Hash(Password) };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    /// <summary>Un cont de echipă cu 2FA configurat prin fluxul real; întoarce secretul și codurile de recuperare.</summary>
    private async Task<(User User, string Secret, IReadOnlyList<string> Codes)> EnrolledStaff(UserRole role)
    {
        User owner = await _db.Users.FirstOrDefaultAsync(u => u.IsOwner) ?? await Staff(UserRole.Admin, owner: true);
        string token = await InviteToken(owner, role);
        LoginResponse accepted = (await Accept(token, Password)).Value;
        TwoFactorSetupDto setup = (await new StartTwoFactorSetupCommandHandler(_db, _secrets, _clock).Handle(new StartTwoFactorSetupCommand(accepted.ChallengeToken!), CancellationToken.None)).Value;
        TwoFactorEnabledDto enabled = (await Confirm(accepted.ChallengeToken!, Totp.CodeAt(setup.Secret, Totp.StepAt(_clock.UtcNow)))).Value;
        return (await _db.Users.SingleAsync(u => u.Id == accepted.UserId), setup.Secret, enabled.RecoveryCodes);
    }

    private async Task<User> AcceptAndEnroll(string token)
    {
        LoginResponse accepted = (await Accept(token, Password)).Value;
        TwoFactorSetupDto setup = (await new StartTwoFactorSetupCommandHandler(_db, _secrets, _clock).Handle(new StartTwoFactorSetupCommand(accepted.ChallengeToken!), CancellationToken.None)).Value;
        (await Confirm(accepted.ChallengeToken!, Totp.CodeAt(setup.Secret, Totp.StepAt(_clock.UtcNow)))).IsSuccess.ShouldBeTrue();
        return await _db.Users.SingleAsync(u => u.Id == accepted.UserId);
    }

    private static FixedUser As(User user) => new(user.Id);

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; }
    }

    private sealed class Tokens : ITokenProvider
    {
        public string Create(User user) => $"access-{user.Id}";

        public string CreateRefreshToken() => Guid.NewGuid().ToString("N");
    }

    private sealed class Renderer : IMjmlRenderer
    {
        public string Render(string mjml) => mjml;
    }

    private sealed class Mail : IEmailService
    {
        public List<string> To { get; } = [];

        public List<string> Links { get; } = [];

        public Task<Result> SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            To.Add(to);
            int start = htmlBody.IndexOf("https://app.test/invitatie/", StringComparison.Ordinal);
            if (start >= 0)
            {
                Links.Add(new string([.. htmlBody[start..].TakeWhile(ch => ch != '"')]));
            }

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
