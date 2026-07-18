using SharedKernel;

namespace Domain.Payments;

/// <summary>
/// Tracks a fiscal invoice generated in Oblio for a payment
/// (subscription charge, one-time service or public service order).
/// </summary>
public sealed class IssuedInvoice : Entity
{
    public Guid Id { get; set; }

    /// <summary>The internal payment this invoice was issued for (null for test invoices).</summary>
    public Guid? PaymentRecordId { get; set; }

    /// <summary>The public service order this invoice was issued for, if any.</summary>
    public Guid? ServiceOrderId { get; set; }

    public Guid? UserId { get; set; }

    public string ClientName { get; set; } = string.Empty;
    public string? ClientCif { get; set; }
    public string? ClientEmail { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Amount in bani (RON minor units).</summary>
    public long AmountBani { get; set; }
    public string Currency { get; set; } = "RON";

    /// <summary>Oblio invoice series, e.g. "RDL".</summary>
    public string? SeriesName { get; set; }

    /// <summary>Oblio invoice number within the series.</summary>
    public string? Number { get; set; }

    /// <summary>Public link to the generated invoice document.</summary>
    public string? Link { get; set; }

    public IssuedInvoiceStatus Status { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>True for invoices generated from the admin test section.</summary>
    public bool IsTest { get; set; }

    /// <summary>Invoices are NOT sent to SPV (e-Factura) for now; kept for later.</summary>
    public bool SentToSpv { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum IssuedInvoiceStatus
{
    Issued = 0,
    Failed = 1,
}
