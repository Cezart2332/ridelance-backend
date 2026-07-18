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

public sealed record GetBankTransactionsQuery(int? Year, int? Month, int Page, int PageSize)
    : IQuery<BankTransactionsResponse>;

internal sealed class GetBankTransactionsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetBankTransactionsQuery, BankTransactionsResponse>
{
    public async Task<Result<BankTransactionsResponse>> Handle(
        GetBankTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        int pageSize = Math.Clamp(query.PageSize, 1, 100);
        int page = Math.Max(query.Page, 1);

        IQueryable<BankTransaction> transactions = context.BankTransactions
            .AsNoTracking()
            .Where(bt => bt.UserId == userId);

        if (query.Year is int year && query.Month is int month)
        {
            var from = new DateOnly(year, Math.Clamp(month, 1, 12), 1);
            DateOnly to = from.AddMonths(1);
            transactions = transactions.Where(bt =>
                bt.BookingDate != null && bt.BookingDate >= from && bt.BookingDate < to);
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
