using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Admin.Oblio;

public sealed record GetIssuedInvoicesQuery(int Limit = 50) : IQuery<List<IssuedInvoiceDto>>;

public sealed record IssuedInvoiceDto(
    Guid Id,
    string ClientName,
    string? ClientCif,
    string Description,
    long AmountBani,
    string Currency,
    string? SeriesName,
    string? Number,
    string? Link,
    string Status,
    string? ErrorMessage,
    bool IsTest,
    bool SentToSpv,
    DateTime CreatedAtUtc);

internal sealed class GetIssuedInvoicesQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetIssuedInvoicesQuery, List<IssuedInvoiceDto>>
{
    public async Task<Result<List<IssuedInvoiceDto>>> Handle(
        GetIssuedInvoicesQuery query,
        CancellationToken cancellationToken)
    {
        int limit = Math.Clamp(query.Limit, 1, 200);

        List<IssuedInvoiceDto> invoices = await context.IssuedInvoices
            .AsNoTracking()
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(limit)
            .Select(i => new IssuedInvoiceDto(
                i.Id,
                i.ClientName,
                i.ClientCif,
                i.Description,
                i.AmountBani,
                i.Currency,
                i.SeriesName,
                i.Number,
                i.Link,
                i.Status.ToString(),
                i.ErrorMessage,
                i.IsTest,
                i.SentToSpv,
                i.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return invoices;
    }
}
