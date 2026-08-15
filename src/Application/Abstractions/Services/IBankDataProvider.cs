namespace Application.Abstractions.Services;

/// <summary>
/// Partea de date a unui provider de open banking: ce conturi există și ce s-a întâmplat în ele.
///
/// Interfața e modelată pe ce poate face efectiv un provider, nu pe un flux anume de autorizare.
/// Partea de autorizare cu redirect stă separat, în <see cref="IBankRedirectAuthorization"/>,
/// tocmai ca un provider care nu are așa ceva să nu fie obligat să întoarcă valori inventate.
/// </summary>
public interface IBankDataProvider
{
    string ProviderName { get; }

    bool IsConfigured { get; }

    Task<IReadOnlyList<BankInstitutionInfo>> ListInstitutionsAsync(
        string countryCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creează linkul din browser prin care utilizatorul își conectează banca.
    /// </summary>
    /// <param name="institutionId">
    /// Banca preselectată, dacă utilizatorul a ales una. Null lasă alegerea în ecranul providerului.
    /// </param>
    Task<BankLinkCreated> MintConnectionLinkAsync(
        string? institutionId,
        CancellationToken cancellationToken = default);

    /// <summary>Reia autorizarea unei conexiuni existente, păstrându-i identitatea.</summary>
    Task<BankLinkCreated> MintReconnectLinkAsync(
        string providerConnectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Toate conexiunile vizibile cu credențialele configurate.
    ///
    /// Cu un token de cont, lista le conține pe ale tuturor utilizatorilor. Cine o consumă e
    /// responsabil să nu scurgă din ea nimic care nu aparține celui care întreabă.
    /// </summary>
    Task<IReadOnlyList<BankProviderConnection>> ListConnectionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListAccountsAsync(
        string providerConnectionId,
        CancellationToken cancellationToken = default);

    Task<BankAccountDetailsInfo> GetAccountDetailsAsync(
        string providerAccountId,
        CancellationToken cancellationToken = default);

    Task<BankTransactionsPage> GetTransactionsAsync(
        string providerAccountId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken = default);

    /// <summary>Cere providerului o sincronizare imediată. Best-effort: eșecul nu e fatal.</summary>
    Task TriggerSyncAsync(
        string providerConnectionId,
        CancellationToken cancellationToken = default);

    Task DeleteConnectionAsync(
        string providerConnectionId,
        CancellationToken cancellationToken = default);
}

public sealed record BankInstitutionInfo(
    string Id,
    string Name,
    string? Logo);

/// <param name="Address">Linkul de browser pe care îl deschide utilizatorul.</param>
/// <param name="ExpiresAtUtc">Linkul e de unică folosință și expiră; null când providerul nu spune când.</param>
public sealed record BankLinkCreated(
    string Address,
    DateTime? ExpiresAtUtc);

/// <summary>O conexiune așa cum o vede providerul, înainte să știm al cui e.</summary>
public sealed record BankProviderConnection(
    string Id,
    string? InstitutionId,
    string? InstitutionName,
    string? InstitutionLogo,
    string? Status,
    DateTime? CreatedAtUtc);

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

/// <summary>Consimțământul a expirat sau a fost revocat — utilizatorul trebuie să reautorizeze.</summary>
public sealed class BankDataConsentExpiredException(string message)
    : BankDataProviderException(message);
