using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Admin.GoCardless;

public sealed record GetGoCardlessStatusQuery : IQuery<GoCardlessStatusResponse>;

public sealed record GoCardlessStatusResponse(
    bool Configured,
    string Provider,
    bool ConnectionOk,
    int InstitutionsAvailable,
    int LinkedConnections,
    int ExpiredConnections,
    int ErrorConnections,
    int TotalAccounts,
    int TotalTransactions,
    DateTime? LastSyncedAtUtc,
    string? Error);

internal sealed class GetGoCardlessStatusQueryHandler(
    IApplicationDbContext context,
    IBankDataProvider provider)
    : IQueryHandler<GetGoCardlessStatusQuery, GoCardlessStatusResponse>
{
    public async Task<Result<GoCardlessStatusResponse>> Handle(
        GetGoCardlessStatusQuery query,
        CancellationToken cancellationToken)
    {
        List<BankConnection> connections = await context.BankConnections
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        int totalAccounts = await context.BankAccounts.CountAsync(cancellationToken);
        int totalTransactions = await context.BankTransactions.CountAsync(cancellationToken);
        DateTime? lastSync = connections.Max(c => c.LastSyncedAtUtc);

        bool connectionOk = false;
        int institutionsAvailable = 0;
        string? error = null;

        if (!provider.IsConfigured)
        {
            error = $"Lipsesc datele de configurare pentru providerul bancar ({provider.ProviderName}).";
        }
        else
        {
            try
            {
                IReadOnlyList<BankInstitutionInfo> institutions =
                    await provider.ListInstitutionsAsync("ro", cancellationToken);
                connectionOk = true;
                institutionsAvailable = institutions.Count;
            }
            catch (BankDataProviderException ex)
            {
                error = ex.Message;
            }
        }

        return new GoCardlessStatusResponse(
            provider.IsConfigured,
            provider.ProviderName,
            connectionOk,
            institutionsAvailable,
            connections.Count(c => c.Status == BankConnectionStatus.Linked),
            connections.Count(c => c.Status == BankConnectionStatus.Expired),
            connections.Count(c => c.Status == BankConnectionStatus.Error),
            totalAccounts,
            totalTransactions,
            lastSync,
            error);
    }
}
