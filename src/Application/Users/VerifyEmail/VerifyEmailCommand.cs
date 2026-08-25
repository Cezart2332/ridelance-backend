using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.VerifyEmail;

/// <summary>Confirmă adresa de email cu codul primit.</summary>
public sealed record VerifyEmailCommand(string Email, string Code) : ICommand;

internal sealed class VerifyEmailCommandHandler(IApplicationDbContext context)
    : ICommandHandler<VerifyEmailCommand>
{
    public async Task<Result> Handle(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        string email = command.Email.Trim();

        User? user = await context.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Aceeași eroare pentru cont inexistent și pentru cod greșit: altfel, formularul ar spune
        // oricui care adrese au cont pe platformă.
        if (user is null)
        {
            return Result.Failure(UserErrors.VerificationCodeInvalid);
        }

        if (user.IsEmailVerified)
        {
            // Deja confirmat nu e o eroare — cine apasă de două ori vrea același rezultat.
            return Result.Success();
        }

        if (user.EmailVerificationCode is null || user.EmailVerificationCodeExpiresAtUtc is null)
        {
            return Result.Failure(UserErrors.VerificationCodeMissing);
        }

        if (user.EmailVerificationCodeExpiresAtUtc < DateTime.UtcNow)
        {
            return Result.Failure(UserErrors.VerificationCodeExpired);
        }

        if (user.EmailVerificationAttempts >= EmailVerification.MaxAttempts)
        {
            return Result.Failure(UserErrors.VerificationTooManyAttempts);
        }

        if (!string.Equals(user.EmailVerificationCode, command.Code.Trim(), StringComparison.Ordinal))
        {
            user.EmailVerificationAttempts++;
            await context.SaveChangesAsync(cancellationToken);
            return Result.Failure(UserErrors.VerificationCodeInvalid);
        }

        user.EmailVerifiedAtUtc = DateTime.UtcNow;
        // Codul dispare: unul consumat nu mai are voie să confirme a doua oară.
        user.EmailVerificationCode = null;
        user.EmailVerificationCodeExpiresAtUtc = null;
        user.EmailVerificationAttempts = 0;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
