using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Payments.GetPaymentHistory;

internal sealed class GetPaymentHistoryQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetPaymentHistoryQuery, List<PaymentHistoryItem>>
{
    public async Task<Result<List<PaymentHistoryItem>>> Handle(
        GetPaymentHistoryQuery query,
        CancellationToken cancellationToken)
    {
        List<PaymentHistoryItem> items = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.UserId == query.UserId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new PaymentHistoryItem(
                p.Id,
                p.PaymentType.ToString(),
                p.Status.ToString(),
                p.AmountBani,
                p.Description,
                p.StripePaymentId,
                p.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return items;
    }
}
