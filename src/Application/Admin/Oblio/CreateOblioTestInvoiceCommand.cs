using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Payments;
using SharedKernel;

namespace Application.Admin.Oblio;

public sealed record CreateOblioTestInvoiceCommand(
    string? ClientName,
    decimal AmountLei,
    string? Description) : ICommand<OblioTestInvoiceResponse>;

public sealed record OblioTestInvoiceResponse(
    string SeriesName,
    string Number,
    string Link);

internal sealed class CreateOblioTestInvoiceCommandHandler(
    IApplicationDbContext context,
    IOblioService oblioService)
    : ICommandHandler<CreateOblioTestInvoiceCommand, OblioTestInvoiceResponse>
{
    public async Task<Result<OblioTestInvoiceResponse>> Handle(
        CreateOblioTestInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        if (!oblioService.IsConfigured)
        {
            return Result.Failure<OblioTestInvoiceResponse>(Error.Problem(
                "Oblio.NotConfigured",
                "Integrarea Oblio nu este configurată. Setează Oblio:ClientId, Oblio:ClientSecret, Oblio:Cif și Oblio:SeriesName."));
        }

        if (command.AmountLei <= 0 || command.AmountLei > 100000)
        {
            return Result.Failure<OblioTestInvoiceResponse>(Error.Problem(
                "Oblio.InvalidAmount",
                "Suma pentru factura de test trebuie să fie între 0.01 și 100000 lei."));
        }

        string clientName = string.IsNullOrWhiteSpace(command.ClientName)
            ? "Client Test RIDElance"
            : command.ClientName.Trim();

        string description = string.IsNullOrWhiteSpace(command.Description)
            ? "Factură de test RIDElance"
            : command.Description.Trim();

        var invoice = new IssuedInvoice
        {
            Id = Guid.NewGuid(),
            ClientName = clientName,
            Description = description,
            AmountBani = (long)Math.Round(command.AmountLei * 100, MidpointRounding.AwayFromZero),
            IsTest = true,
            CreatedAtUtc = DateTime.UtcNow,
        };

        try
        {
            OblioInvoiceResult result = await oblioService.CreateInvoiceAsync(
                new OblioInvoiceClient(clientName),
                [new OblioInvoiceLine(description, command.AmountLei)],
                internalNote: "Factură de test generată din dashboardul de admin RIDElance",
                cancellationToken);

            invoice.Status = IssuedInvoiceStatus.Issued;
            invoice.SeriesName = result.SeriesName;
            invoice.Number = result.Number;
            invoice.Link = result.Link;

            context.IssuedInvoices.Add(invoice);
            await context.SaveChangesAsync(cancellationToken);

            return new OblioTestInvoiceResponse(result.SeriesName, result.Number, result.Link);
        }
        catch (OblioApiException ex)
        {
            invoice.Status = IssuedInvoiceStatus.Failed;
            invoice.ErrorMessage = ex.Message;

            context.IssuedInvoices.Add(invoice);
            await context.SaveChangesAsync(cancellationToken);

            return Result.Failure<OblioTestInvoiceResponse>(Error.Problem("Oblio.CreateFailed", ex.Message));
        }
    }
}
