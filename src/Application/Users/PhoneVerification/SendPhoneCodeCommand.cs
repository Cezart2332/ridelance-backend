using System.Globalization;
using System.Security.Cryptography;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.PhoneVerification;

/// <summary>
/// Trimite prin SMS un cod de confirmare pe numărul contului.
/// </summary>
/// <remarks>
/// Numărul poate veni în comandă — atunci se și salvează pe cont, fiindcă cel care confirmă un
/// număr nou îl vrea pe acela, nu pe cel vechi. Fără el, se folosește numărul deja salvat.
/// </remarks>
public sealed record SendPhoneCodeCommand(string? PhoneNumber = null) : ICommand;

internal sealed class SendPhoneCodeCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    ISmsService smsService) : ICommandHandler<SendPhoneCodeCommand>
{
    public async Task<Result> Handle(SendPhoneCodeCommand command, CancellationToken cancellationToken)
    {
        User? user = await context.Users
            .FirstOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound(userContext.UserId));
        }

        string? requested = command.PhoneNumber?.Trim();
        string? target = string.IsNullOrWhiteSpace(requested) ? user.PhoneNumber : requested;

        if (string.IsNullOrWhiteSpace(target))
        {
            return Result.Failure(UserErrors.PhoneMissing);
        }

        string? international = RomanianPhoneNumber.ToInternational(target);
        if (international is null)
        {
            return Result.Failure(UserErrors.PhoneInvalid);
        }

        // Pauza se măsoară din momentul emiterii, dedus din expirare — la fel ca la email, ca să
        // nu apară o a doua coloană care spune același lucru.
        DateTime? issuedAt = user.PhoneVerificationCodeExpiresAtUtc?.Subtract(Domain.Users.PhoneVerification.CodeLifetime);
        if (issuedAt.HasValue && DateTime.UtcNow - issuedAt.Value < Domain.Users.PhoneVerification.ResendCooldown)
        {
            return Result.Failure(UserErrors.VerificationResendTooSoon);
        }

        // Numărul nou intră pe cont odată cu codul, iar confirmarea veche cade: altfel bifa ar
        // rămâne pe un număr care nu mai e al contului.
        if (!string.Equals(user.PhoneNumber, target, StringComparison.Ordinal))
        {
            user.PhoneNumber = target;
            user.PhoneVerifiedAtUtc = null;
        }

        if (user.IsPhoneVerified)
        {
            // Deja confirmat: nu are rost un SMS plătit ca să afle ce știe.
            return Result.Success();
        }

        string code = Issue(user);
        await context.SaveChangesAsync(cancellationToken);

        string message = string.Create(
            CultureInfo.InvariantCulture,
            $"Codul tău RIDElance este {code}. Expiră în {(int)Domain.Users.PhoneVerification.CodeLifetime.TotalMinutes} minute.");

        return await smsService.SendAsync(international, message, cancellationToken);
    }

    /// <summary>
    /// Pune un cod nou pe cont și îi resetează încercările.
    /// </summary>
    /// <remarks>
    /// Cifrele vin din generatorul criptografic, ca la email: un cod ghicibil din cel anterior
    /// n-ar mai confirma nimic.
    /// </remarks>
    private static string Issue(User user)
    {
        int max = (int)Math.Pow(10, Domain.Users.PhoneVerification.CodeLength);
        string code = RandomNumberGenerator
            .GetInt32(max)
            .ToString(CultureInfo.InvariantCulture)
            .PadLeft(Domain.Users.PhoneVerification.CodeLength, '0');

        user.PhoneVerificationCode = code;
        user.PhoneVerificationCodeExpiresAtUtc = DateTime.UtcNow.Add(Domain.Users.PhoneVerification.CodeLifetime);
        user.PhoneVerificationAttempts = 0;

        return code;
    }
}
