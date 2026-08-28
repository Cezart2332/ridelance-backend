using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.PhoneVerification;

/// <summary>Confirmă numărul de telefon cu codul primit prin SMS.</summary>
public sealed record ConfirmPhoneCommand(string Code) : ICommand;

internal sealed class ConfirmPhoneCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext) : ICommandHandler<ConfirmPhoneCommand>
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

        user.PhoneVerifiedAtUtc = DateTime.UtcNow;
        // Codul dispare: unul consumat nu mai are voie să confirme a doua oară.
        user.PhoneVerificationCode = null;
        user.PhoneVerificationCodeExpiresAtUtc = null;
        user.PhoneVerificationAttempts = 0;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
