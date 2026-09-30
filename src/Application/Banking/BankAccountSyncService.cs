using Application.Abstractions.Data;
using Application.Abstractions.Services;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;

namespace Application.Banking;

/// <summary>
/// Sincronizează tranzacțiile unui cont bancar de la provider în DB (upsert idempotent).
/// Folosit atât de sync-ul inline de după linkare, cât și de BankSyncJob.
/// Aruncă excepțiile tipizate ale providerului — apelantul decide cum le tratează.
/// </summary>
public sealed class BankAccountSyncService(
    IApplicationDbContext context,
    IBankDataProvider provider)
{
    /// <summary>Suprapunere la re-sync pentru tranzacții înregistrate târziu de bancă.</summary>
    private static readonly TimeSpan ResyncOverlap = TimeSpan.FromDays(5);

    public async Task SyncAccountAsync(
        Domain.Banking.BankAccount account,
        BankConnection connection,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // O sincronizare veche putea fi marcată reușită deși apelul fără dateTo nu aducea
        // tranzacții. Pentru conturile încă goale reluăm istoricul de la început.
        bool hasTransactions = await context.BankTransactions
            .AnyAsync(bt => bt.BankAccountId == account.Id &&
                bt.ProviderConsentId == connection.ProviderConsentId, cancellationToken);
        DateOnly dateFrom = hasTransactions && account.LastTransactionsSyncedAtUtc is DateTime lastSync
            ? DateOnly.FromDateTime(lastSync - ResyncOverlap)
            : today.AddDays(-Math.Max(1, connection.MaxHistoricalDays));

        BankTransactionsPage page = await provider.GetTransactionsAsync(
            connection.InstitutionId,
            connection.ProviderConsentId,
            account.ProviderAccountId,
            dateFrom,
            // Smart Accounts cere ambele capete. Paginarea păstrează exact același interval.
            dateTo: today,
            cancellationToken);

        // Pending-urile sunt tranzitorii: le ștergem și le reinserăm pe cele curente.
        List<BankTransaction> oldPending = await context.BankTransactions
            .Where(bt => bt.BankAccountId == account.Id &&
                bt.ProviderConsentId == connection.ProviderConsentId && bt.IsPending)
            .ToListAsync(cancellationToken);
        context.BankTransactions.RemoveRange(oldPending);

        var incomingIds = page.Booked.Concat(page.Pending)
            .Select(tx => tx.ProviderTransactionId)
            .ToList();

        var existingIds = (await context.BankTransactions
                .Where(bt => bt.BankAccountId == account.Id &&
                    bt.ProviderConsentId == connection.ProviderConsentId &&
                    incomingIds.Contains(bt.ProviderTransactionId))
                .Select(bt => bt.ProviderTransactionId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        // Rândurile pending tocmai șterse nu mai blochează reinserarea.
        foreach (BankTransaction pending in oldPending)
        {
            existingIds.Remove(pending.ProviderTransactionId);
        }

        AddNewTransactions(page.Booked, isPending: false, account, connection.ProviderConsentId, existingIds);
        AddNewTransactions(page.Pending, isPending: true, account, connection.ProviderConsentId, existingIds);
        if (page.Balance is { } balance)
        {
            account.Balance = balance.Amount;
            account.BalanceDate = balance.Date;
        }

        account.LastTransactionsSyncedAtUtc = DateTime.UtcNow;
        connection.LastSyncedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
    }

    private void AddNewTransactions(
        IReadOnlyList<BankTransactionInfo> transactions,
        bool isPending,
        Domain.Banking.BankAccount account,
        string providerConsentId,
        HashSet<string> existingIds)
    {
        foreach (BankTransactionInfo tx in transactions)
        {
            if (!existingIds.Add(tx.ProviderTransactionId))
            {
                continue;
            }

            context.BankTransactions.Add(new BankTransaction
            {
                Id = Guid.NewGuid(),
                BankAccountId = account.Id,
                UserId = account.UserId,
                ProviderConsentId = providerConsentId,
                ProviderTransactionId = tx.ProviderTransactionId,
                BookingDate = tx.BookingDate,
                ValueDate = tx.ValueDate,
                Amount = tx.Amount,
                Currency = tx.Currency,
                CounterpartyName = tx.CounterpartyName,
                CounterpartyIban = tx.CounterpartyIban,
                RemittanceInfo = tx.RemittanceInfo,
                IsPending = isPending,
                RawJson = tx.RawJson,
                ImportedAtUtc = DateTime.UtcNow,
            });
        }
    }
}
