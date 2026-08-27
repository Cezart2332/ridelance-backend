using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Bolt;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Bolt.Queries;

public sealed record BoltIntegrationResponse(
    Guid Id,
    /// <summary>
    /// Doar pentru afișare: `4wEssh...ME`, nu un Client ID cu care se poate autentifica nimeni.
    /// Numele spune asta pentru că a fost nevoie — formularul îl reciclase ca valoare inițială și
    /// îl trimitea înapoi la salvare, suprascriind Client ID-ul real cu masca lui.
    /// </summary>
    string ClientIdMasked,
    int CompanyId,
    string? CompanyName,
    bool IsConnected,
    string? ErrorMessage,
    DateTime? LastFetchedAtUtc);

public sealed record GetBoltIntegrationQuery : IQuery<BoltIntegrationResponse?>;

internal sealed class GetBoltIntegrationQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext) : IQueryHandler<GetBoltIntegrationQuery, BoltIntegrationResponse?>
{
    public async Task<Result<BoltIntegrationResponse?>> Handle(
        GetBoltIntegrationQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        BoltIntegration? integration = await context.BoltIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(bi => bi.UserId == userId, cancellationToken);

        if (integration == null)
        {
            return Result.Success<BoltIntegrationResponse?>(null);
        }

        // Masca de afișare. Nu se întoarce niciodată Client ID-ul întreg: interfața n-are ce face
        // cu el, iar orice câmp care îl primește îl poate trimite înapoi.
        string maskedClientId = integration.ClientId.Length > 8
            ? integration.ClientId[..6] + "..." + integration.ClientId[^2..]
            : "••••••••";

        var response = new BoltIntegrationResponse(
            integration.Id,
            maskedClientId,
            integration.CompanyId,
            integration.CompanyName,
            integration.IsConnected,
            integration.ErrorMessage,
            integration.LastFetchedAtUtc);

        return response;
    }
}
