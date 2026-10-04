using Application.Abstractions.Data;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;

namespace Application.Accounting.Ledger;

/// <summary>
/// Tranzacțiile bancare ale unui PFA, o singură definiție pentru importul în ledger și controlul de
/// sold al lunii (QA 3): conexiunea declarată în onboarding (altfel toate conturile utilizatorului),
/// legată, consimțământul ei curent, conturi active, tranzacții rezervate, cu dată și sumă.
/// </summary>
internal static class PfaBankTransactions
{
    public static async Task<IQueryable<BankTransaction>> QueryAsync(IApplicationDbContext db, Guid pfaId, Guid userId, CancellationToken cancellationToken)
    {
        Guid? connectionId = await db.PfaBankAccountDeclarations
            .Where(d => d.PfaRegistrationId == pfaId)
            .Select(d => d.BankConnectionId)
            .FirstOrDefaultAsync(cancellationToken);

        IQueryable<BankTransaction> transactions = db.BankTransactions.AsNoTracking()
            .Where(t => !t.IsPending && (t.BookingDate != null || t.ValueDate != null) && t.Amount != 0 &&
                        t.UserId == userId && t.Account.UserId == userId && t.Account.IsActive &&
                        t.Account.Connection.Status == BankConnectionStatus.Linked &&
                        t.ProviderConsentId == t.Account.Connection.ProviderConsentId);
        return connectionId is { } connection ? transactions.Where(t => t.Account.BankConnectionId == connection) : transactions;
    }
}
