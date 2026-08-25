using Application.Abstractions;
using Application.Abstractions.Data;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Application.Users.Register;

/// <summary>
/// Trimite codul de confirmare, imediat după ce contul a fost creat.
/// </summary>
/// <remarks>
/// Rulează după <c>SaveChangesAsync</c>, deci codul emis în handlerul de înregistrare e deja
/// scris. Un eșec de trimitere nu anulează contul: e deja creat, iar codul poate fi cerut din nou.
/// </remarks>
internal sealed class UserRegisteredDomainEventHandler(
    IApplicationDbContext context,
    IEmailService emailService,
    IMjmlRenderer mjmlRenderer,
    ILogger<UserRegisteredDomainEventHandler> logger) : IDomainEventHandler<UserRegisteredDomainEvent>
{
    public async Task Handle(UserRegisteredDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        User? user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == domainEvent.UserId, cancellationToken);

        if (user?.EmailVerificationCode is null)
        {
            return;
        }

        Result result = await EmailVerificationEmail.SendAsync(
            emailService,
            mjmlRenderer,
            user.Email,
            user.FirstName,
            user.EmailVerificationCode,
            cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Codul de confirmare pentru {Email} nu a putut fi trimis: {Error}",
                user.Email,
                result.Error.Description);
        }
    }
}
