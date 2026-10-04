using SharedKernel;

namespace Application.Abstractions.Services;

public sealed record FiscalLinkCashReceipt(string Serial, string Number, DateTime IssuedAtUtc, decimal Total, string ExternalId);

public sealed record FiscalLinkCashZ(string Serial, string Number, DateTime IssuedAtUtc, decimal Total, string ExternalId, string Json);

public sealed record FiscalLinkCashDocuments(
    IReadOnlyList<FiscalLinkCashReceipt> Receipts,
    IReadOnlyList<FiscalLinkCashZ> Reports,
    IReadOnlyList<string> Notes);

/// <summary>Read-only import of completed cloud commands belonging to a client's registers.</summary>
public interface IFiscalLinkAccountingService
{
    bool IsConfigured { get; }

    Task<Result<FiscalLinkCashDocuments>> ReadAsync(Guid clientId, CancellationToken cancellationToken = default);
}
