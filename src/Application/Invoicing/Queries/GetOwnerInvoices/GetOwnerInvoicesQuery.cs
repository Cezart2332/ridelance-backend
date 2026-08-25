using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Invoicing.Commands.ConnectOblio;
using Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Invoicing.Queries.GetOwnerInvoices;

/// <param name="From">Începutul intervalului. Implicit, prima zi a lunii trecute.</param>
public sealed record GetOwnerInvoicesQuery(DateOnly? From = null, DateOnly? To = null)
    : IQuery<OwnerInvoicesDto>;

/// <param name="Status">`paid`, `partial`, `unpaid` sau `canceled` — derivat, nu stocat.</param>
#pragma warning disable CA1054
public sealed record OwnerInvoiceDto(
    string SeriesName,
    string Number,
    DateOnly IssueDate,
    DateOnly? DueDate,
    string ClientName,
    string? ClientCif,
    long TotalBani,
    long CollectedBani,
    string? Link,
    string Status,
    bool Overdue);
#pragma warning restore CA1054

/// <summary>Cifrele de sus ale paginii, calculate din același set pe care îl vede tabelul.</summary>
public sealed record OwnerInvoiceSummaryDto(
    long IssuedBani,
    int IssuedCount,
    long CollectedBani,
    int CollectedCount,
    long OutstandingBani,
    int OverdueCount);

public sealed record OwnerInvoicesDto(
    OblioConnectionDto Connection,
    OwnerInvoiceSummaryDto Summary,
    List<OwnerInvoiceDto> Invoices);

internal sealed class GetOwnerInvoicesQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    OwnerOblioResolver resolver,
    IOwnerInvoicingService invoicing)
    : IQueryHandler<GetOwnerInvoicesQuery, OwnerInvoicesDto>
{
    public async Task<Result<OwnerInvoicesDto>> Handle(
        GetOwnerInvoicesQuery query,
        CancellationToken cancellationToken)
    {
        OblioIntegration? integration = await context.OblioIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.UserId == userContext.UserId, cancellationToken);

        // Contul neconectat nu e o eroare: e starea în care pornește oricine. Pagina arată
        // invitația de conectare, nu un ecran roșu.
        if (integration is null || !integration.IsConnected)
        {
            return Result.Success(new OwnerInvoicesDto(
                new OblioConnectionDto(false, null, null, null, [], integration?.ErrorMessage, null),
                new OwnerInvoiceSummaryDto(0, 0, 0, 0, 0, 0),
                []));
        }

        Result<OwnerOblioCredentials> credentials = await resolver.ResolveAsync(userContext.UserId, cancellationToken);
        if (credentials.IsFailure)
        {
            return Result.Failure<OwnerInvoicesDto>(credentials.Error);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly from = query.From ?? new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        DateOnly to = query.To ?? today;

        IReadOnlyList<OwnerInvoice> invoices;
        try
        {
            invoices = await invoicing.ListInvoicesAsync(credentials.Value, from, to, cancellationToken);
        }
        catch (OblioApiException ex)
        {
            return Result.Failure<OwnerInvoicesDto>(Error.Problem("Oblio.ListFailed", ex.Message));
        }

        var dtos = invoices.Select(invoice => Map(invoice, today)).ToList();

        var connection = new OblioConnectionDto(
            true,
            integration.CompanyName,
            integration.Cif,
            integration.SeriesName,
            [],
            null,
            integration.LastSyncAtUtc);

        return Result.Success(new OwnerInvoicesDto(connection, Summarise(dtos), dtos));
    }

    private static OwnerInvoiceDto Map(OwnerInvoice invoice, DateOnly today)
    {
        // Sumele se țin în bani peste tot în platformă; Oblio le dă în lei.
        long total = ToBani(invoice.TotalLei);
        long collected = ToBani(invoice.CollectedLei);

        string status = DeriveStatus(invoice.Canceled, total, collected);

        bool overdue = status is "unpaid" or "partial"
            && invoice.DueDate is not null
            && invoice.DueDate < today;

        return new OwnerInvoiceDto(
            invoice.SeriesName,
            invoice.Number,
            invoice.IssueDate,
            invoice.DueDate,
            invoice.ClientName,
            invoice.ClientCif,
            total,
            collected,
            invoice.Link,
            status,
            overdue);
    }

    /// <summary>
    /// Statusul se derivă din sume, nu se stochează: Oblio e sursa adevărului pentru încasări,
    /// iar o copie locală ar fi rămas în urmă la prima încasare făcută direct în contul lor.
    /// </summary>
    private static string DeriveStatus(bool canceled, long total, long collected)
    {
        if (canceled)
        {
            return "canceled";
        }

        if (total > 0 && collected >= total)
        {
            return "paid";
        }

        return collected > 0 ? "partial" : "unpaid";
    }

    private static OwnerInvoiceSummaryDto Summarise(IReadOnlyCollection<OwnerInvoiceDto> invoices)
    {
        // Anulatele ies din toate cifrele: o factură stornată nu e nici facturată, nici de încasat.
        var live = invoices.Where(i => i.Status != "canceled").ToList();

        return new OwnerInvoiceSummaryDto(
            live.Sum(i => i.TotalBani),
            live.Count,
            live.Sum(i => i.CollectedBani),
            live.Count(i => i.Status == "paid"),
            live.Sum(i => i.TotalBani - i.CollectedBani),
            live.Count(i => i.Overdue));
    }

    private static long ToBani(decimal lei) => (long)Math.Round(lei * 100, MidpointRounding.AwayFromZero);
}
