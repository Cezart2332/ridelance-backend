namespace Application.Abstractions.Services;

/// <summary>
/// Abstracts the Oblio.eu invoicing API (https://www.oblio.eu/api).
/// Invoices are only generated in Oblio — they are NOT sent to SPV (e-Factura).
/// </summary>
public interface IOblioService
{
    /// <summary>True when ClientId, ClientSecret, Cif and SeriesName are all configured.</summary>
    bool IsConfigured { get; }

    /// <summary>The company CIF invoices are issued for (from configuration).</summary>
    string? Cif { get; }

    /// <summary>The invoice series used for generated invoices (from configuration).</summary>
    string? SeriesName { get; }

    /// <summary>
    /// Authenticates and reads company + invoice series nomenclature to verify the setup.
    /// Throws OblioApiException on failure.
    /// </summary>
    Task<OblioConnectionInfo> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an invoice in Oblio. Does NOT send it to SPV.
    /// Throws OblioApiException on failure.
    /// </summary>
    Task<OblioInvoiceResult> CreateInvoiceAsync(
        OblioInvoiceClient client,
        IReadOnlyList<OblioInvoiceLine> lines,
        string? internalNote = null,
        CancellationToken cancellationToken = default);
}

public sealed record OblioInvoiceClient(
    string Name,
    string? Cif = null,
    string? Email = null,
    string? Address = null,
    string? City = null,
    string? State = null);

/// <summary>One invoice line. Price is in lei, VAT included.</summary>
public sealed record OblioInvoiceLine(string Name, decimal PriceLei, decimal Quantity = 1);

public sealed record OblioInvoiceResult(string SeriesName, string Number, string Link);

public sealed record OblioConnectionInfo(
    string CompanyName,
    string Cif,
    IReadOnlyList<string> InvoiceSeries);

/// <summary>Raised when the Oblio API rejects a request or is unreachable.</summary>
#pragma warning disable CA1032, CA1064 // intentional minimal exception surface
public sealed class OblioApiException(string message, Exception? inner = null)
    : Exception(message, inner);
#pragma warning restore CA1032, CA1064
