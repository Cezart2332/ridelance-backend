using Application.Abstractions.Data;
using Application.Abstractions.Services;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Invoicing;

/// <summary>
/// Issues Oblio invoices for completed transactions. Never throws:
/// failures are stored as IssuedInvoice rows with status Failed so they
/// are visible in the admin section, without breaking payment processing.
/// </summary>
internal sealed class InvoiceGenerator(
    IApplicationDbContext context,
    IOblioService oblioService,
    ILogger<InvoiceGenerator> logger) : IInvoiceGenerator
{
    public async Task GenerateForPaymentRecordAsync(Guid paymentRecordId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!oblioService.IsConfigured)
            {
                logger.LogDebug("Oblio is not configured — skipping invoice for payment {PaymentId}.", paymentRecordId);
                return;
            }

            PaymentRecord? record = await context.PaymentRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == paymentRecordId, cancellationToken);

            if (record is null || record.Status != PaymentStatus.Succeeded || record.AmountBani <= 0)
            {
                return;
            }

            bool alreadyIssued = await context.IssuedInvoices
                .AnyAsync(i => i.PaymentRecordId == paymentRecordId && i.Status == IssuedInvoiceStatus.Issued, cancellationToken);

            if (alreadyIssued)
            {
                return;
            }

            User? user = await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == record.UserId, cancellationToken);

            PfaRegistration? pfa = await context.PfaRegistrations
                .AsNoTracking()
                .Where(p => p.UserId == record.UserId)
                .OrderByDescending(p => p.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            string clientName = ResolveClientName(pfa, user);

            string? address = pfa is null || string.IsNullOrWhiteSpace(pfa.Street)
                ? null
                : $"Str. {pfa.Street} nr. {pfa.Number}".Trim();

            var client = new OblioInvoiceClient(
                Name: clientName,
                Cif: pfa?.Cui,
                Email: user?.Email,
                Address: address,
                City: pfa?.City,
                State: pfa?.County);

            await IssueAsync(
                client,
                record.Description,
                record.AmountBani,
                paymentRecordId: record.Id,
                serviceOrderId: null,
                userId: record.UserId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while generating Oblio invoice for payment {PaymentId}.", paymentRecordId);
        }
    }

    public async Task GenerateForServiceOrderAsync(Guid serviceOrderId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!oblioService.IsConfigured)
            {
                logger.LogDebug("Oblio is not configured — skipping invoice for service order {OrderId}.", serviceOrderId);
                return;
            }

            ServiceOrder? order = await context.ServiceOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == serviceOrderId, cancellationToken);

            if (order is null || order.Status != ServiceOrderStatus.Paid || (order.AmountBani ?? 0) <= 0)
            {
                return;
            }

            bool alreadyIssued = await context.IssuedInvoices
                .AnyAsync(i => i.ServiceOrderId == serviceOrderId && i.Status == IssuedInvoiceStatus.Issued, cancellationToken);

            if (alreadyIssued)
            {
                return;
            }

            var client = new OblioInvoiceClient(
                Name: order.CustomerName,
                Email: order.CustomerEmail);

            await IssueAsync(
                client,
                order.ServiceTitle,
                order.AmountBani!.Value,
                paymentRecordId: null,
                serviceOrderId: order.Id,
                userId: null,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while generating Oblio invoice for service order {OrderId}.", serviceOrderId);
        }
    }

    private static string ResolveClientName(PfaRegistration? pfa, User? user)
    {
        if (!string.IsNullOrWhiteSpace(pfa?.FullName))
        {
            return pfa.FullName;
        }

        return user is null ? "Client RIDElance" : $"{user.FirstName} {user.LastName}";
    }

    private async Task IssueAsync(
        OblioInvoiceClient client,
        string description,
        long amountBani,
        Guid? paymentRecordId,
        Guid? serviceOrderId,
        Guid? userId,
        CancellationToken ct)
    {
        var invoice = new IssuedInvoice
        {
            Id = Guid.NewGuid(),
            PaymentRecordId = paymentRecordId,
            ServiceOrderId = serviceOrderId,
            UserId = userId,
            ClientName = client.Name,
            ClientCif = client.Cif,
            ClientEmail = client.Email,
            Description = description,
            AmountBani = amountBani,
            CreatedAtUtc = DateTime.UtcNow,
        };

        try
        {
            decimal amountLei = amountBani / 100m;
            OblioInvoiceResult result = await oblioService.CreateInvoiceAsync(
                client,
                [new OblioInvoiceLine(description, amountLei)],
                internalNote: paymentRecordId is not null
                    ? $"RIDElance payment {paymentRecordId}"
                    : $"RIDElance service order {serviceOrderId}",
                ct);

            invoice.Status = IssuedInvoiceStatus.Issued;
            invoice.SeriesName = result.SeriesName;
            invoice.Number = result.Number;
            invoice.Link = result.Link;

            logger.LogInformation(
                "Oblio invoice {Series}-{Number} issued for {Description} ({AmountBani} bani).",
                result.SeriesName, result.Number, description, amountBani);
        }
        catch (OblioApiException ex)
        {
            invoice.Status = IssuedInvoiceStatus.Failed;
            invoice.ErrorMessage = ex.Message;
            logger.LogError(ex, "Oblio invoice generation failed for {Description}.", description);
        }

        context.IssuedInvoices.Add(invoice);
        await context.SaveChangesAsync(ct);
    }
}
