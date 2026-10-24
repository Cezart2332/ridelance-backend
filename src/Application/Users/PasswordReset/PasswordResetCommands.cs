using System.Globalization;
using System.Security.Cryptography;
using Application.Abstractions;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Users.TwoFactor;
using Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.PasswordReset;

/// <summary>Trimite pe email un cod de resetare a parolei.</summary>
public sealed record RequestPasswordResetCommand(string Email) : ICommand;

/// <summary>Setează o parolă nouă cu codul primit pe email. Închide toate sesiunile deschise.</summary>
public sealed record ResetPasswordCommand(string Email, string Code, string NewPassword) : ICommand;

internal sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty();
        RuleFor(c => c.Code).NotEmpty();
        RuleFor(c => c.NewPassword).NotEmpty().MinimumLength(Domain.Users.PasswordReset.MinPasswordLength)
            .WithMessage($"Parola nouă trebuie să aibă cel puțin {Domain.Users.PasswordReset.MinPasswordLength} caractere.");
    }
}

internal sealed class RequestPasswordResetCommandHandler(
    IApplicationDbContext context,
    IEmailService emailService,
    IMjmlRenderer mjmlRenderer) : ICommandHandler<RequestPasswordResetCommand>
{
    public async Task<Result> Handle(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        string email = command.Email.Trim();
        User? user = await context.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Succes și pentru o adresă fără cont (sau un cont închis): un răspuns diferit ar spune
        // oricui ce adrese sunt înregistrate pe platformă.
        if (user is null || user.IsDeleted)
        {
            return Result.Success();
        }

        DateTime? issuedAt = user.PasswordResetCodeExpiresAtUtc?.Subtract(Domain.Users.PasswordReset.CodeLifetime);
        if (issuedAt.HasValue && DateTime.UtcNow - issuedAt.Value < Domain.Users.PasswordReset.ResendCooldown)
        {
            return Result.Failure(UserErrors.VerificationResendTooSoon);
        }

        int max = (int)Math.Pow(10, Domain.Users.PasswordReset.CodeLength);
        string code = RandomNumberGenerator.GetInt32(max)
            .ToString(CultureInfo.InvariantCulture)
            .PadLeft(Domain.Users.PasswordReset.CodeLength, '0');

        user.PasswordResetCodeHash = TwoFactorSupport.Hash(code);
        user.PasswordResetCodeExpiresAtUtc = DateTime.UtcNow.Add(Domain.Users.PasswordReset.CodeLifetime);
        user.PasswordResetAttempts = 0;
        await context.SaveChangesAsync(cancellationToken);

        return await PasswordResetEmail.SendAsync(emailService, mjmlRenderer, user.Email, user.FirstName, code, cancellationToken);
    }
}

internal sealed class ResetPasswordCommandHandler(
    IApplicationDbContext context,
    IPasswordHasher passwordHasher) : ICommandHandler<ResetPasswordCommand>
{
    public async Task<Result> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        string email = command.Email.Trim();
        User? user = await context.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Aceeași eroare pentru cont inexistent și cod greșit.
        if (user is null || user.IsDeleted)
        {
            return Result.Failure(UserErrors.VerificationCodeInvalid);
        }

        if (user.PasswordResetCodeHash is null || user.PasswordResetCodeExpiresAtUtc is null)
        {
            return Result.Failure(UserErrors.VerificationCodeMissing);
        }

        if (user.PasswordResetCodeExpiresAtUtc < DateTime.UtcNow)
        {
            return Result.Failure(UserErrors.VerificationCodeExpired);
        }

        if (user.PasswordResetAttempts >= Domain.Users.PasswordReset.MaxAttempts)
        {
            return Result.Failure(UserErrors.VerificationTooManyAttempts);
        }

        string given = TwoFactorSupport.Hash(command.Code.Trim());
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(given),
                System.Text.Encoding.ASCII.GetBytes(user.PasswordResetCodeHash)))
        {
            user.PasswordResetAttempts++;
            await context.SaveChangesAsync(cancellationToken);
            return Result.Failure(UserErrors.VerificationCodeInvalid);
        }

        user.PasswordHash = passwordHasher.Hash(command.NewPassword);
        user.PasswordResetCodeHash = null;
        user.PasswordResetCodeExpiresAtUtc = null;
        user.PasswordResetAttempts = 0;

        // Cine a schimbat parola pentru că o crede compromisă trebuie să-i scoată pe toți ceilalți.
        user.RefreshToken = null;
        user.RefreshTokenExpiryUtc = null;

        // Codul a ajuns în inbox și a fost citit: adresa e a lui.
        user.EmailVerifiedAtUtc ??= DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
