using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Invoicing;

/// <summary>
/// Aduce credențialele Oblio ale unui proprietar și le decriptează.
/// </summary>
/// <remarks>
/// Într-un singur loc, fiindcă fiecare comandă care vorbește cu Oblio are nevoie de exact
/// aceiași pași — găsește integrarea, verifică dacă e conectată, decriptează secretul — iar
/// repetarea lor ar fi însemnat, mai devreme sau mai târziu, o cale care uită verificarea.
/// </remarks>
public sealed class OwnerOblioResolver(IApplicationDbContext context, ISecretProtector secrets)
{
    public static readonly Error NotConnected = Error.Problem(
        "Oblio.NotConnected",
        "Contul Oblio nu e conectat. Adaugă-l din Conexiuni.");

    public async Task<Result<OwnerOblioCredentials>> ResolveAsync(Guid userId, CancellationToken cancellationToken)
    {
        OblioIntegration? integration = await context.OblioIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.UserId == userId, cancellationToken);

        if (integration is null || !integration.IsConnected)
        {
            return Result.Failure<OwnerOblioCredentials>(NotConnected);
        }

        return Result.Success(new OwnerOblioCredentials(
            integration.ClientId,
            secrets.Unprotect(integration.ClientSecretEncrypted),
            integration.Cif));
    }
}
