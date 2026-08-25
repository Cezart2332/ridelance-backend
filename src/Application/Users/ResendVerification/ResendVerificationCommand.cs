using Application.Abstractions;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Users.Register;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.ResendVerification;

/// <summary>Trimite din nou codul de confirmare, cu unul nou.</summary>
public sealed record ResendVerificationCommand(string Email) : ICommand;

internal sealed class ResendVerificationCommandHandler(
    IApplicationDbContext context,
    IEmailService emailService,
    IMjmlRenderer mjmlRenderer) : ICommandHandler<ResendVerificationCommand>
{
    public async Task<Result> Handle(ResendVerificationCommand command, CancellationToken cancellationToken)
    {
        string email = command.Email.Trim();

        User? user = await context.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Succes și când adresa n-are cont: un răspuns diferit ar fi transformat butonul de
        // retrimitere într-un mod de a afla cine e înregistrat.
        if (user is null || user.IsEmailVerified)
        {
            return Result.Success();
        }

        DateTime? issuedAt = user.EmailVerificationCodeExpiresAtUtc?.Subtract(EmailVerification.CodeLifetime);
        if (issuedAt.HasValue && DateTime.UtcNow - issuedAt.Value < EmailVerification.ResendCooldown)
        {
            return Result.Failure(UserErrors.VerificationResendTooSoon);
        }

        string code = EmailVerificationCodes.Issue(user);
        await context.SaveChangesAsync(cancellationToken);

        return await EmailVerificationEmail.SendAsync(
            emailService,
            mjmlRenderer,
            user.Email,
            user.FirstName,
            code,
            cancellationToken);
    }
}
