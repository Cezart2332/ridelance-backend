using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.PhoneVerification;

/// <summary>Confirmă numărul de telefon cu codul primit prin SMS.</summary>
public sealed record ConfirmPhoneCommand(string Code) : ICommand;

internal sealed class ConfirmPhoneCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IPhoneCodeVerifier codeVerifier) : ICommandHandler<ConfirmPhoneCommand>
{
    public async Task<Result> Handle(ConfirmPhoneCommand command, CancellationToken cancellationToken)
    {
        User? user = await context.Users
            .FirstOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound(userContext.UserId));
        }

        if (user.IsPhoneVerified)
        {
            // Deja confirmat nu e o eroare — cine apasă de două ori vrea același rezultat.
            return Result.Success();
        }

        if (codeVerifier.IsEnabled)
        {
            return await ConfirmWithProviderAsync(user, command.Code.Trim(), cancellationToken);
        }

        if (user.PhoneVerificationCode is null || user.PhoneVerificationCodeExpiresAtUtc is null)
        {
            return Result.Failure(UserErrors.VerificationCodeMissing);
        }

        if (user.PhoneVerificationCodeExpiresAtUtc < DateTime.UtcNow)
        {
            return Result.Failure(UserErrors.VerificationCodeExpired);
        }

        if (user.PhoneVerificationAttempts >= Domain.Users.PhoneVerification.MaxAttempts)
        {
            return Result.Failure(UserErrors.VerificationTooManyAttempts);
        }

        if (!string.Equals(user.PhoneVerificationCode, command.Code.Trim(), StringComparison.Ordinal))
        {
            user.PhoneVerificationAttempts++;
            await context.SaveChangesAsync(cancellationToken);
            return Result.Failure(UserErrors.VerificationCodeInvalid);
        }

        await MarkVerifiedAsync(user, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Codul trimis prin Twilio Verify se verifică tot la Twilio. Fereastra și încercările le ținem
    /// și noi, ca mesajele să fie aceleași ca pe drumul vechi.
    /// </summary>
    private async Task<Result> ConfirmWithProviderAsync(User user, string code, CancellationToken cancellationToken)
    {
        if (user.PhoneVerificationCodeExpiresAtUtc is null || user.PhoneNumber is null)
        {
            return Result.Failure(UserErrors.VerificationCodeMissing);
        }

        if (user.PhoneVerificationCodeExpiresAtUtc < DateTime.UtcNow)
        {
            return Result.Failure(UserErrors.VerificationCodeExpired);
        }

        if (user.PhoneVerificationAttempts >= Domain.Users.PhoneVerification.MaxAttempts)
        {
            return Result.Failure(UserErrors.VerificationTooManyAttempts);
        }

        string? international = RomanianPhoneNumber.ToInternational(user.PhoneNumber);
        if (international is null)
        {
            return Result.Failure(UserErrors.PhoneInvalid);
        }

        Result<PhoneCodeCheck> check = await codeVerifier.CheckCodeAsync(international, code, cancellationToken);
        if (check.IsFailure)
        {
            return Result.Failure(check.Error);
        }

        switch (check.Value)
        {
            case PhoneCodeCheck.Approved:
                await MarkVerifiedAsync(user, cancellationToken);
                return Result.Success();
            case PhoneCodeCheck.Expired:
                return Result.Failure(UserErrors.VerificationCodeExpired);
            default:
                user.PhoneVerificationAttempts++;
                await context.SaveChangesAsync(cancellationToken);
                return Result.Failure(UserErrors.VerificationCodeInvalid);
        }
    }

    private async Task MarkVerifiedAsync(User user, CancellationToken cancellationToken)
    {
        user.PhoneVerifiedAtUtc = DateTime.UtcNow;
        // Codul dispare: unul consumat nu mai are voie să confirme a doua oară.
        user.PhoneVerificationCode = null;
        user.PhoneVerificationCodeExpiresAtUtc = null;
        user.PhoneVerificationAttempts = 0;

        await context.SaveChangesAsync(cancellationToken);
    }
}
