namespace Application.Abstractions.Services;

/// <summary>
/// Provider-agnostic open-banking (PSD2 AIS) data provider.
/// Current implementation: GoCardless Bank Account Data. The rest of the system
/// must depend only on this interface so the provider can be swapped
/// (e.g. Enable Banking, Salt Edge) without touching handlers, jobs or endpoints.
/// </summary>
public interface IBankDataProvider
{
    string ProviderName { get; }

    bool IsConfigured { get; }

    Task<IReadOnlyList<BankInstitutionInfo>> ListInstitutionsAsync(
        string countryCode,
        CancellationToken cancellationToken = default);

    Task<BankRequisitionCreated> CreateRequisitionAsync(
        string institutionId,
        string redirectAddress,
        string reference,
        int maxHistoricalDays,
        int accessValidForDays,
        CancellationToken cancellationToken = default);

    /// <param name="authorizationCode">
    /// Codul one-time întors de provider la redirect (ex. Enable Banking ?code=);
    /// null pentru providerii care nu folosesc schimb de cod (GoCardless).
    /// </param>
    Task<BankRequisitionDetails> GetRequisitionAsync(
        string requisitionId,
        string? authorizationCode = null,
        CancellationToken cancellationToken = default);

    Task DeleteRequisitionAsync(
        string requisitionId,
        CancellationToken cancellationToken = default);

    Task<BankAccountDetailsInfo> GetAccountDetailsAsync(
        string providerAccountId,
        CancellationToken cancellationToken = default);

    Task<BankTransactionsPage> GetTransactionsAsync(
        string providerAccountId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken = default);
}

public sealed record BankInstitutionInfo(
    string Id,
    string Name,
    string? Logo,
    int MaxHistoricalDays);

public sealed record BankRequisitionCreated(
    string RequisitionId,
    string? AgreementId,
    string AuthorizationLink);

public enum BankRequisitionStatus
{
    Created,
    GivingConsent,
    UndergoingAuthentication,
    Linked,
    Expired,
    Rejected,
    Suspended,
}

/// <param name="UpdatedRequisitionId">
/// Setat când providerul schimbă identificatorul în timpul finalizării
/// (ex. Enable Banking: authorization_id devine session_id); se persistă în locul celui vechi.
/// </param>
public sealed record BankRequisitionDetails(
    BankRequisitionStatus Status,
    IReadOnlyList<string> AccountIds,
    DateTime? ConsentExpiresAtUtc,
    string? UpdatedRequisitionId = null);

public sealed record BankAccountDetailsInfo(
    string? Iban,
    string? Currency,
    string? OwnerName);

public sealed record BankTransactionInfo(
    string ProviderTransactionId,
    DateOnly? BookingDate,
    DateOnly? ValueDate,
    decimal Amount,
    string Currency,
    string? CounterpartyName,
    string? RemittanceInfo,
    string RawJson);

public sealed record BankTransactionsPage(
    IReadOnlyList<BankTransactionInfo> Booked,
    IReadOnlyList<BankTransactionInfo> Pending);

/// <summary>Generic provider error (network, HTTP, unexpected payload).</summary>
public class BankDataProviderException : Exception
{
    public BankDataProviderException(string message)
        : base(message)
    {
    }

    public BankDataProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Bank/provider rate limit hit (HTTP 429) — retry after the given delay.</summary>
public sealed class BankDataRateLimitException(string message, TimeSpan? retryAfter)
    : BankDataProviderException(message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>PSD2 consent expired or was revoked — the user must re-authorize.</summary>
public sealed class BankDataConsentExpiredException(string message)
    : BankDataProviderException(message);
