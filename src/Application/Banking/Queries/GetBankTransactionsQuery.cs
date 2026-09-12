using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Banking.Queries;

public sealed record BankTransactionResponse(
    Guid Id,
    DateOnly? BookingDate,
    decimal Amount,
    string Currency,
    string? CounterpartyName,
    string? RemittanceInfo,
    bool IsPending);

public sealed record BankTransactionsResponse(
    List<BankTransactionResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    decimal TotalIn,
    decimal TotalOut);

/// <summary>
/// Tranzacțiile dintr-un interval, paginate.
///
/// Intervalul vine gata calculat din interfață, nu ca „an + lună" ca până acum: selectorul de
/// perioadă are patru trepte (zi, săptămână, lună, an), iar an+lună nu le poate exprima pe toate.
/// Fără interval se întorc toate.
///
/// <paramref name="TargetUserId"/> e pentru contabil: fără el se citește contul propriu.
/// </summary>
public sealed record GetBankTransactionsQuery(
    DateOnly? From,
    DateOnly? To,
    int Page,
    int PageSize,
    Guid? TargetUserId = null) : IQuery<BankTransactionsResponse>;

internal sealed class GetBankTransactionsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetBankTransactionsQuery, BankTransactionsResponse>
{
    public async Task<Result<BankTransactionsResponse>> Handle(
        GetBankTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        Result<Guid> owner = await BankAccess.ResolveAsync(
            context, userContext.UserId, query.TargetUserId, cancellationToken);

        if (owner.IsFailure)
        {
            return Result.Failure<BankTransactionsResponse>(owner.Error);
        }

        Guid userId = owner.Value;

        int pageSize = Math.Clamp(query.PageSize, 1, 100);
        int page = Math.Max(query.Page, 1);

        IQueryable<BankTransaction> transactions = context.BankTransactions
            .AsNoTracking()
            .Where(bt => bt.UserId == userId);

        // Capete inclusive: „luna martie" înseamnă și 31 martie.
        if (query.From is DateOnly from)
        {
            transactions = transactions.Where(bt => bt.BookingDate != null && bt.BookingDate >= from);
        }

        if (query.To is DateOnly to)
        {
            transactions = transactions.Where(bt => bt.BookingDate != null && bt.BookingDate <= to);
        }

        int totalCount = await transactions.CountAsync(cancellationToken);

        decimal totalIn = await transactions
            .Where(bt => bt.Amount > 0)
            .SumAsync(bt => bt.Amount, cancellationToken);
        decimal totalOut = await transactions
            .Where(bt => bt.Amount < 0)
            .SumAsync(bt => bt.Amount, cancellationToken);

        List<BankTransactionResponse> items = await transactions
            .OrderByDescending(bt => bt.BookingDate)
            .ThenByDescending(bt => bt.ImportedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(bt => new BankTransactionResponse(
                bt.Id,
                bt.BookingDate,
                bt.Amount,
                bt.Currency,
                bt.CounterpartyName,
                bt.RemittanceInfo,
                bt.IsPending))
            .ToListAsync(cancellationToken);

        return new BankTransactionsResponse(items, totalCount, page, pageSize, totalIn, Math.Abs(totalOut));
    }
}
