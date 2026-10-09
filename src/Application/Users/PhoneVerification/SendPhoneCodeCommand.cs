using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.PhoneVerification;

/// <summary>
/// Trimite prin SMS un cod de confirmare pe numărul contului, prin Twilio Verify.
/// </summary>
/// <remarks>
/// Numărul poate veni în comandă — atunci se și salvează pe cont, fiindcă cel care confirmă un
/// număr nou îl vrea pe acela, nu pe cel vechi. Fără el, se folosește numărul deja salvat.
/// </remarks>
public sealed record SendPhoneCodeCommand(string? PhoneNumber = null) : ICommand;

internal sealed class SendPhoneCodeCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IPhoneCodeVerifier codeVerifier) : ICommandHandler<SendPhoneCodeCommand>
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

        // Codul îl generează, îl trimite și îl verifică Twilio. Noi ținem doar fereastra de
        // valabilitate (pentru pauza dintre retrimiteri) și numărul de încercări.
        user.PhoneVerificationCodeExpiresAtUtc = DateTime.UtcNow.Add(Domain.Users.PhoneVerification.CodeLifetime);
        user.PhoneVerificationAttempts = 0;
        await context.SaveChangesAsync(cancellationToken);

        return await codeVerifier.SendCodeAsync(international, cancellationToken);
    }
}
