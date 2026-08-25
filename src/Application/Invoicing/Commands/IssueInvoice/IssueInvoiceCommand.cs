using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using SharedKernel;

namespace Application.Invoicing.Commands.IssueInvoice;

/// <summary>
/// Emite o factură pe contul Oblio al proprietarului, fără ca el să intre în Oblio.
/// </summary>
/// <param name="Lines">Cel puțin una. O factură fără linii n-are ce total să poarte.</param>
/// <param name="DueDateDays">
/// Scadența ca număr de zile de la emitere, nu ca dată: aceeași factură emisă mâine ar fi purtat
/// altfel o scadență deja trecută, iar „la 30 de zile" e oricum forma în care se convine.
/// </param>
public sealed record IssueInvoiceCommand(
    string SeriesName,
    string ClientName,
    string? ClientCif,
    string? ClientEmail,
    string? ClientAddress,
    string? ClientCity,
    string? ClientState,
    int DueDateDays,
    IReadOnlyList<IssueInvoiceLine> Lines,
    string? Note,
    bool SendToSpv) : ICommand<IssuedInvoiceResult>;

/// <param name="PriceBani">Prețul unitar, în bani. Fracțiile de leu contează pe o factură.</param>
public sealed record IssueInvoiceLine(
    string Name,
    decimal Quantity,
    long PriceBani,
    string MeasuringUnit,
    decimal VatPercent,
    bool VatIncluded);

/// <summary>Factura așa cum a numerotat-o Oblio.</summary>
#pragma warning disable CA1054 // `Link` e URL-ul public al documentului, ca șir — îl afișăm, nu-l construim
public sealed record IssuedInvoiceResult(string SeriesName, string Number, string? Link);
#pragma warning restore CA1054

internal sealed class IssueInvoiceCommandHandler(
    IUserContext userContext,
    OwnerOblioResolver resolver,
    IOwnerInvoicingService invoicing)
    : ICommandHandler<IssueInvoiceCommand, IssuedInvoiceResult>
{
    /// <summary>
    /// Eticheta cotei, cerută de Oblio alături de procent.
    /// </summary>
    /// <remarks>
    /// API-ul refuză un procent fără nume, iar numele sunt fixe la ei. Un procent care nu e în
    /// listă e o cotă care nu există în România, deci se oprește aici, nu la Oblio.
    /// </remarks>
    private static readonly Dictionary<decimal, string> VatNames = new()
    {
        [21m] = "Normala",
        [19m] = "Normala",
        [11m] = "Redusa",
        [9m] = "Redusa",
        [5m] = "Redusa",
        [0m] = "SFDD",
    };

    public async Task<Result<IssuedInvoiceResult>> Handle(IssueInvoiceCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ClientName))
        {
            return Result.Failure<IssuedInvoiceResult>(
                Error.Problem("Invoice.ClientRequired", "Numele clientului e obligatoriu."));
        }

        if (command.Lines.Count == 0)
        {
            return Result.Failure<IssuedInvoiceResult>(
                Error.Problem("Invoice.LinesRequired", "Adaugă cel puțin o linie pe factură."));
        }

        foreach (IssueInvoiceLine line in command.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Name))
            {
                return Result.Failure<IssuedInvoiceResult>(
                    Error.Problem("Invoice.LineNameRequired", "Fiecare linie are nevoie de o denumire."));
            }

            if (line.Quantity <= 0 || line.PriceBani <= 0)
            {
                return Result.Failure<IssuedInvoiceResult>(
                    Error.Problem("Invoice.LineInvalid", $"Cantitatea și prețul de la „{line.Name}” trebuie să fie pozitive."));
            }

            if (!VatNames.ContainsKey(line.VatPercent))
            {
                return Result.Failure<IssuedInvoiceResult>(
                    Error.Problem("Invoice.VatUnsupported", $"Cota de TVA {line.VatPercent}% nu e una acceptată."));
            }
        }

        if (command.DueDateDays < 0)
        {
            return Result.Failure<IssuedInvoiceResult>(
                Error.Problem("Invoice.DueDateInvalid", "Scadența nu poate fi înaintea emiterii."));
        }

        Result<OwnerOblioCredentials> credentials = await resolver.ResolveAsync(userContext.UserId, cancellationToken);
        if (credentials.IsFailure)
        {
            return Result.Failure<IssuedInvoiceResult>(credentials.Error);
        }

        // Ziua de emitere e cea din România, nu UTC: o factură emisă la 01:30 noaptea ar fi purtat
        // altfel data de ieri, iar numerotarea într-o serie trebuie să urce odată cu datele.
        var issueDate = DateOnly.FromDateTime(RomaniaToday());

        var invoice = new NewOwnerInvoice(
            command.SeriesName,
            command.ClientName.Trim(),
            string.IsNullOrWhiteSpace(command.ClientCif) ? null : command.ClientCif.Trim(),
            string.IsNullOrWhiteSpace(command.ClientEmail) ? null : command.ClientEmail.Trim(),
            command.ClientAddress,
            command.ClientCity,
            command.ClientState,
            issueDate,
            command.DueDateDays > 0 ? issueDate.AddDays(command.DueDateDays) : null,
            command.Lines
                .Select(line => new NewInvoiceLine(
                    line.Name.Trim(),
                    line.Quantity,
                    line.PriceBani / 100m,
                    string.IsNullOrWhiteSpace(line.MeasuringUnit) ? "buc" : line.MeasuringUnit.Trim(),
                    line.VatPercent,
                    VatNames[line.VatPercent],
                    line.VatIncluded))
                .ToList(),
            command.Note,
            command.SendToSpv);

        try
        {
            OwnerInvoice created = await invoicing.CreateInvoiceAsync(credentials.Value, invoice, cancellationToken);
            return new IssuedInvoiceResult(created.SeriesName, created.Number, created.Link);
        }
        catch (OblioApiException ex)
        {
            return Result.Failure<IssuedInvoiceResult>(Error.Problem("Invoice.IssueFailed", ex.Message));
        }
    }

    private static DateTime RomaniaToday()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
    }
}
