namespace Application.Abstractions.Services;

/// <summary>
/// Generates Oblio invoices for completed transactions and records the outcome
/// as IssuedInvoice rows. Implementations must never throw — a failed invoice
/// generation must not break payment processing; failures are stored with
/// <c>IssuedInvoiceStatus.Failed</c> instead.
/// </summary>
public interface IInvoiceGenerator
{
    /// <summary>Generates an invoice for a succeeded PaymentRecord (subscription or one-time).</summary>
    Task GenerateForPaymentRecordAsync(Guid paymentRecordId, CancellationToken cancellationToken = default);

    /// <summary>Generates an invoice for a paid public ServiceOrder (guest checkout).</summary>
    Task GenerateForServiceOrderAsync(Guid serviceOrderId, CancellationToken cancellationToken = default);
}
