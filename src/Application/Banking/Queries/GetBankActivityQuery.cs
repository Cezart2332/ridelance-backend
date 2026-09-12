using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Banking.Queries;

/// <summary>Cât a intrat și cât a ieșit într-o felie de timp — o coloană din grafic.</summary>
public sealed record BankActivityBucket(DateOnly Start, decimal In, decimal Out, int Count);

/// <summary>Un cont legat, cu ultima sincronizare — ca să se vadă cât de proaspete sunt datele.</summary>
public sealed record BankActivityAccount(
    Guid Id,
    string? IbanMasked,
    string? Currency,
    string? OwnerName,
    DateTime? LastSyncedAtUtc);

public sealed record BankActivityResponse(
    DateOnly From,
    DateOnly To,
    string Bucket,
    decimal TotalIn,
    decimal TotalOut,
    int TransactionCount,
    List<BankActivityBucket> Buckets,
    List<BankActivityAccount> Accounts);

/// <summary>
/// Mișcările din cont pe un interval, grupate — ce înlocuiește extrasul de cont.
///
/// Intervalul vine gata calculat din interfață (zi, săptămână, lună, an), nu ca „an + lună" ca
/// până acum: selectorul de perioadă are patru trepte, iar an+lună nu le poate exprima pe toate.
/// Gruparea o alege tot apelantul — pe un an vrei coloane lunare, pe o lună vrei zile.
///
/// <paramref name="TargetUserId"/> e pentru contabil: fără el se citește contul propriu.
/// </summary>
public sealed record GetBankActivityQuery(
    DateOnly From,
    DateOnly To,
    string Bucket,
    Guid? TargetUserId = null) : IQuery<BankActivityResponse>;

internal sealed class GetBankActivityQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetBankActivityQuery, BankActivityResponse>
{
    public async Task<Result<BankActivityResponse>> Handle(
        GetBankActivityQuery query,
        CancellationToken cancellationToken)
    {
        Result<Guid> owner = await BankAccess.ResolveAsync(
            context, userContext.UserId, query.TargetUserId, cancellationToken);

        if (owner.IsFailure)
        {
            return Result.Failure<BankActivityResponse>(owner.Error);
        }

        Guid userId = owner.Value;

        // Intervalul e inclusiv la ambele capete: „luna martie" înseamnă și 31 martie.
        DateOnly from = query.From <= query.To ? query.From : query.To;
        DateOnly to = query.From <= query.To ? query.To : query.From;

        List<BankTransaction> transactions = await context.BankTransactions
            .AsNoTracking()
            .Where(bt => bt.UserId == userId
                && bt.BookingDate != null
                && bt.BookingDate >= from
                && bt.BookingDate <= to)
            .ToListAsync(cancellationToken);

        List<BankActivityAccount> accounts = await context.BankAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.IsActive)
            .OrderBy(a => a.IbanMasked)
            .Select(a => new BankActivityAccount(
                a.Id,
                a.IbanMasked,
                a.Currency,
                a.OwnerName,
                a.LastTransactionsSyncedAtUtc))
            .ToListAsync(cancellationToken);

        string bucket = Normalize(query.Bucket);

        List<BankActivityBucket> buckets = [.. transactions
            .GroupBy(bt => BucketStart(bt.BookingDate!.Value, bucket))
            .OrderBy(g => g.Key)
            .Select(g => new BankActivityBucket(
                g.Key,
                g.Where(bt => bt.Amount > 0).Sum(bt => bt.Amount),
                Math.Abs(g.Where(bt => bt.Amount < 0).Sum(bt => bt.Amount)),
                g.Count()))];

        return new BankActivityResponse(
            from,
            to,
            bucket,
            transactions.Where(bt => bt.Amount > 0).Sum(bt => bt.Amount),
            Math.Abs(transactions.Where(bt => bt.Amount < 0).Sum(bt => bt.Amount)),
            transactions.Count,
            buckets,
            accounts);
    }

    private static string Normalize(string bucket) => bucket?.ToUpperInvariant() switch
    {
        "WEEK" => "week",
        "MONTH" => "month",
        _ => "day",
    };

    /// <summary>Săptămâna începe luni: așa o citește toată lumea aici, și așa o taie și ANAF-ul.</summary>
    private static DateOnly BucketStart(DateOnly date, string bucket) => bucket switch
    {
        "week" => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        "month" => new DateOnly(date.Year, date.Month, 1),
        _ => date,
    };
}
