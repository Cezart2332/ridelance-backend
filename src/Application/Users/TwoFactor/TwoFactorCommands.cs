using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Application.Users.Login;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.TwoFactor;

/// <summary>Secretul (pentru introducere manuală) și textul codului QR (<c>otpauth://…</c>).</summary>
public sealed record TwoFactorSetupDto(string Secret, string QrText);

public sealed record TwoFactorEnabledDto(LoginResponse Session, IReadOnlyList<string> RecoveryCodes);

/// <summary>Configurarea 2FA, pasul 1: secretul nou și adresa pentru codul QR.</summary>
public sealed record StartTwoFactorSetupCommand(string ChallengeToken) : ICommand<TwoFactorSetupDto>;

internal sealed class StartTwoFactorSetupCommandHandler(IApplicationDbContext db, ISecretProtector secrets, IDateTimeProvider clock)
    : ICommandHandler<StartTwoFactorSetupCommand, TwoFactorSetupDto>
{
    public async Task<Result<TwoFactorSetupDto>> Handle(StartTwoFactorSetupCommand command, CancellationToken cancellationToken)
    {
        Result<User> found = await TwoFactorSupport.UserOfChallengeAsync(db, command.ChallengeToken, clock.UtcNow, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<TwoFactorSetupDto>(found.Error);
        }

        User user = found.Value;
        if (user.IsTwoFactorEnabled)
        {
            return Result.Failure<TwoFactorSetupDto>(TwoFactorErrors.AlreadyEnabled);
        }

        string secret = Totp.NewSecret();
        user.TwoFactorPendingSecret = secrets.Protect(secret);
        await db.SaveChangesAsync(cancellationToken);
        return new TwoFactorSetupDto(secret, Totp.ProvisioningText(TwoFactorSupport.Issuer, user.Email, secret));
    }
}

/// <summary>Configurarea 2FA, pasul 2: primul cod din aplicație confirmă secretul; urmează codurile de recuperare și sesiunea.</summary>
public sealed record ConfirmTwoFactorSetupCommand(string ChallengeToken, string Code) : ICommand<TwoFactorEnabledDto>;

internal sealed class ConfirmTwoFactorSetupCommandHandler(IApplicationDbContext db, ISecretProtector secrets, ITokenProvider tokens, IDateTimeProvider clock)
    : ICommandHandler<ConfirmTwoFactorSetupCommand, TwoFactorEnabledDto>
{
    public async Task<Result<TwoFactorEnabledDto>> Handle(ConfirmTwoFactorSetupCommand command, CancellationToken cancellationToken)
    {
        DateTime now = clock.UtcNow;
        Result<User> found = await TwoFactorSupport.UserOfChallengeAsync(db, command.ChallengeToken, now, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<TwoFactorEnabledDto>(found.Error);
        }

        User user = found.Value;
        if (user.IsTwoFactorEnabled)
        {
            return Result.Failure<TwoFactorEnabledDto>(TwoFactorErrors.AlreadyEnabled);
        }

        if (user.TwoFactorPendingSecret is null)
        {
            return Result.Failure<TwoFactorEnabledDto>(TwoFactorErrors.SetupNotStarted);
        }

        if (TwoFactorSupport.IsLocked(user, now))
        {
            return Result.Failure<TwoFactorEnabledDto>(TwoFactorErrors.LockedOut);
        }

        string secret = secrets.Unprotect(user.TwoFactorPendingSecret);
        if (Totp.Match(secret, command.Code, now, null) is not { } step)
        {
            TwoFactorSupport.RegisterFailure(user, now);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure<TwoFactorEnabledDto>(TwoFactorErrors.CodeInvalid);
        }

        user.TwoFactorSecret = user.TwoFactorPendingSecret;
        user.TwoFactorPendingSecret = null;
        user.TwoFactorEnabledAtUtc = now;
        user.TwoFactorLastStep = step;
        IReadOnlyList<string> codes = await TwoFactorSupport.NewRecoveryCodesAsync(db, user.Id, now, cancellationToken);
        LoginResponse session = TwoFactorSupport.IssueSession(user, tokens, now);
        await db.SaveChangesAsync(cancellationToken);
        return new TwoFactorEnabledDto(session, codes);
    }
}

/// <summary>Login, pasul 2: codul din aplicație sau un cod de recuperare (folosit o singură dată).</summary>
public sealed record VerifyTwoFactorCommand(string ChallengeToken, string Code) : ICommand<LoginResponse>;

internal sealed class VerifyTwoFactorCommandHandler(IApplicationDbContext db, ISecretProtector secrets, ITokenProvider tokens, IDateTimeProvider clock)
    : ICommandHandler<VerifyTwoFactorCommand, LoginResponse>
{
    public async Task<Result<LoginResponse>> Handle(VerifyTwoFactorCommand command, CancellationToken cancellationToken)
    {
        DateTime now = clock.UtcNow;
        Result<User> found = await TwoFactorSupport.UserOfChallengeAsync(db, command.ChallengeToken, now, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<LoginResponse>(found.Error);
        }

        User user = found.Value;
        if (!user.IsTwoFactorEnabled)
        {
            return Result.Failure<LoginResponse>(TwoFactorErrors.ChallengeInvalid);
        }

        if (TwoFactorSupport.IsLocked(user, now))
        {
            return Result.Failure<LoginResponse>(TwoFactorErrors.LockedOut);
        }

        bool passed;
        if (TwoFactorSupport.LooksLikeRecoveryCode(command.Code))
        {
            string hash = TwoFactorSupport.Hash(TwoFactorSupport.NormalizeRecovery(command.Code));
            TwoFactorRecoveryCode? recovery = await db.TwoFactorRecoveryCodes
                .SingleOrDefaultAsync(c => c.UserId == user.Id && c.CodeHash == hash && c.UsedAtUtc == null, cancellationToken);
            passed = recovery is not null;
            recovery?.UsedAtUtc = now;
        }
        else
        {
            long? step = Totp.Match(secrets.Unprotect(user.TwoFactorSecret!), command.Code, now, user.TwoFactorLastStep);
            passed = step is not null;
            if (step is not null)
            {
                user.TwoFactorLastStep = step;
            }
        }

        if (!passed)
        {
            TwoFactorSupport.RegisterFailure(user, now);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure<LoginResponse>(TwoFactorSupport.IsLocked(user, now) ? TwoFactorErrors.LockedOut : TwoFactorErrors.CodeInvalid);
        }

        LoginResponse session = TwoFactorSupport.IssueSession(user, tokens, now);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }
}
