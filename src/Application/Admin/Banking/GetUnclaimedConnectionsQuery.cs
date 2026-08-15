using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Admin.Banking;

public sealed record UnclaimedConnection(
    string ProviderConnectionId,
    string? InstitutionName,
    string? Status,
    DateTime? CreatedAtUtc);

public sealed record BankingStatusResponse(
    string Provider,
    bool Configured,
    int TotalConnections,
    int ClaimedConnections,
    List<UnclaimedConnection> Unclaimed,
    string? Error);

public sealed record GetUnclaimedConnectionsQuery : IQuery<BankingStatusResponse>;

/// <summary>
/// Conexiunile din contul de provider care nu au proprietar în baza noastră.
///
/// E mecanismul prin care o revendicare ratată devine vizibilă. Cu un singur cont de provider
/// pentru toți clienții, o conexiune nerevendicată nu e doar o listă în plus: e cineva care a
/// conectat o bancă și nu-și vede datele, sau o conexiune rămasă din greșeală.
/// </summary>
internal sealed class GetUnclaimedConnectionsQueryHandler(
    IApplicationDbContext context,
    IBankDataProvider provider)
    : IQueryHandler<GetUnclaimedConnectionsQuery, BankingStatusResponse>
{
    public async Task<Result<BankingStatusResponse>> Handle(
        GetUnclaimedConnectionsQuery query,
        CancellationToken cancellationToken)
    {
        if (!provider.IsConfigured)
        {
            return new BankingStatusResponse(provider.ProviderName, false, 0, 0, [], null);
        }

        IReadOnlyList<BankProviderConnection> all;
        try
        {
            all = await provider.ListConnectionsAsync(cancellationToken);
        }
        catch (BankDataProviderException ex)
        {
            return new BankingStatusResponse(provider.ProviderName, true, 0, 0, [], ex.Message);
        }

        HashSet<string> claimed = await context.BankConnectionClaims
            .AsNoTracking()
            .Select(c => c.ProviderConnectionId)
            .ToHashSetAsync(cancellationToken);

        List<UnclaimedConnection> unclaimed = [.. all
            .Where(c => !claimed.Contains(c.Id))
            .Select(c => new UnclaimedConnection(c.Id, c.InstitutionName, c.Status, c.CreatedAtUtc))];

        return new BankingStatusResponse(
            provider.ProviderName,
            true,
            all.Count,
            claimed.Count,
            unclaimed,
            null);
    }
}
