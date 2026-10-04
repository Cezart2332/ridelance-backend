using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Users.Login;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.TwoFactor;

public static class TwoFactorErrors
{
    public static readonly Error ChallengeInvalid = Error.Failure(
        "TwoFactor.ChallengeInvalid", "Sesiunea de autentificare a expirat. Autentifică-te din nou.");

    public static readonly Error CodeInvalid = Error.Failure("TwoFactor.CodeInvalid", "Codul nu este corect.");

    public static readonly Error LockedOut = Error.Failure(
        "TwoFactor.LockedOut", "Prea multe coduri greșite. Încearcă din nou peste 15 minute.");

    public static readonly Error AlreadyEnabled = Error.Conflict("TwoFactor.AlreadyEnabled", "Autentificarea în doi pași e deja configurată.");

    public static readonly Error SetupNotStarted = Error.Failure("TwoFactor.SetupNotStarted", "Scanează mai întâi codul QR.");
}

/// <summary>Tokenul de după parolă, codurile de recuperare și emiterea sesiunii.</summary>
internal static class TwoFactorSupport
{
    public const string Verify = "VERIFY";
    public const string Setup = "SETUP";
    public const string Issuer = "RIDElance";

    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);
    public const int RecoveryCodeCount = 10;

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string RandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Pornește pasul 2FA: un token nou (cel vechi nu mai merge), valabil câteva minute.</summary>
    public static LoginResponse StartChallenge(User user, DateTime nowUtc)
    {
        string token = RandomToken();
        user.TwoFactorChallengeHash = Hash(token);
        user.TwoFactorChallengeExpiresAtUtc = nowUtc + ChallengeLifetime;
        return new LoginResponse(string.Empty, string.Empty, user.Role.ToString(), user.Id, user.IsTwoFactorEnabled ? Verify : Setup, token);
    }

    public static async Task<Result<User>> UserOfChallengeAsync(IApplicationDbContext db, string? token, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<User>(TwoFactorErrors.ChallengeInvalid);
        }

        string hash = Hash(token);
        User? user = await db.Users.SingleOrDefaultAsync(u => u.TwoFactorChallengeHash == hash, cancellationToken);
        if (user is null || user.IsDeleted || user.TwoFactorChallengeExpiresAtUtc is not { } expires || expires < nowUtc)
        {
            return Result.Failure<User>(TwoFactorErrors.ChallengeInvalid);
        }

        return user;
    }

    /// <summary>Sesiunea completă, după 2FA: tokenul de acces și cel de refresh; tokenul de pas se consumă.</summary>
    public static LoginResponse IssueSession(User user, ITokenProvider tokens, DateTime nowUtc)
    {
        user.TwoFactorChallengeHash = null;
        user.TwoFactorChallengeExpiresAtUtc = null;
        user.TwoFactorFailedAttempts = 0;
        user.TwoFactorLockedUntilUtc = null;
        string refreshToken = tokens.CreateRefreshToken();
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryUtc = nowUtc.AddDays(7);
        user.LastActivityAtUtc = nowUtc;
        return new LoginResponse(tokens.Create(user), refreshToken, user.Role.ToString(), user.Id);
    }

    /// <summary>Un cod greșit: după <see cref="MaxFailedAttempts"/> contul se blochează temporar.</summary>
    public static void RegisterFailure(User user, DateTime nowUtc)
    {
        user.TwoFactorFailedAttempts++;
        if (user.TwoFactorFailedAttempts >= MaxFailedAttempts)
        {
            user.TwoFactorLockedUntilUtc = nowUtc + Lockout;
            user.TwoFactorFailedAttempts = 0;
        }
    }

    public static bool IsLocked(User user, DateTime nowUtc) => user.TwoFactorLockedUntilUtc is { } until && until > nowUtc;

    /// <summary>Codurile de recuperare noi (cele vechi se șterg): <c>xxxxx-xxxxx</c>, fără litere ambigue.</summary>
    public static async Task<IReadOnlyList<string>> NewRecoveryCodesAsync(IApplicationDbContext db, Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        db.TwoFactorRecoveryCodes.RemoveRange(await db.TwoFactorRecoveryCodes.Where(c => c.UserId == userId).ToListAsync(cancellationToken));
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        var codes = new List<string>();
        for (int i = 0; i < RecoveryCodeCount; i++)
        {
            string raw = new([.. Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)])]);
            string code = $"{raw[..5]}-{raw[5..]}";
            codes.Add(code);
            db.TwoFactorRecoveryCodes.Add(new TwoFactorRecoveryCode { Id = Guid.NewGuid(), UserId = userId, CodeHash = Hash(NormalizeRecovery(code)), CreatedAtUtc = nowUtc });
        }

        return codes;
    }

    public static string NormalizeRecovery(string code) => new([.. code.ToUpperInvariant().Where(char.IsLetterOrDigit)]);

    public static bool LooksLikeRecoveryCode(string code) => NormalizeRecovery(code).Length == 10 && code.Any(char.IsLetter);

    /// <summary>Scoate 2FA-ul unui cont (resetare de către admin sau din configurare): la următorul login îl configurează din nou.</summary>
    public static async Task ClearAsync(IApplicationDbContext db, User user, CancellationToken cancellationToken)
    {
        user.TwoFactorSecret = null;
        user.TwoFactorPendingSecret = null;
        user.TwoFactorEnabledAtUtc = null;
        user.TwoFactorLastStep = null;
        user.TwoFactorFailedAttempts = 0;
        user.TwoFactorLockedUntilUtc = null;
        user.TwoFactorChallengeHash = null;
        user.TwoFactorChallengeExpiresAtUtc = null;
        user.RefreshToken = null;
        user.RefreshTokenExpiryUtc = null;
        db.TwoFactorRecoveryCodes.RemoveRange(await db.TwoFactorRecoveryCodes.Where(c => c.UserId == user.Id).ToListAsync(cancellationToken));
    }
}
