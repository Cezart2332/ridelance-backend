namespace Application.Abstractions.Services;

/// <summary>
/// Furnizorul de open banking, văzut din aplicație.
///
/// Fiecare apel poartă cu el banca și consimțământul, nu doar un id de resursă: la un furnizor
/// PSD2 „contul 123" nu înseamnă nimic singur — există în interiorul unui consimțământ dat unei
/// anume bănci, iar același cont accesat prin două consimțământuri are două identități.
/// </summary>
public interface IBankDataProvider
{
    string ProviderName { get; }

    bool IsConfigured { get; }

    /// <summary>
    /// Băncile disponibile, cu ce cere fiecare înainte de consimțământ. Nu are parametru de țară:
    /// furnizorul e autorizat BNR și listează exclusiv bănci din România.
    /// </summary>
    Task<IReadOnlyList<BankInstitutionInfo>> ListInstitutionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deschide un consimțământ și întoarce adresa băncii unde trimitem utilizatorul.
    ///
    /// Tokenurile vin tot de aici: se emit odată cu consimțământul și sunt valabile numai pentru
    /// el. Cine cheamă metoda e responsabil să le salveze — fără ele consimțământul e de
    /// neatins, oricâte drepturi ar fi dat utilizatorul la bancă.
    /// </summary>
    Task<BankConsentCreated> CreateConsentAsync(
        BankConsentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Starea consimțământului la bancă — cum aflăm că utilizatorul a terminat autorizarea.</summary>
    Task<BankConsentState> GetConsentAsync(
        string bankCode,
        string consentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Conturile acoperite de consimțământ, cu tot cu IBAN și titular — la PSD2 lista le conține
    /// deja, deci nu mai există un al doilea apel pentru detalii.
    /// </summary>
    Task<IReadOnlyList<BankAccountDetailsInfo>> ListAccountsAsync(
        string bankCode,
        string consentId,
        CancellationToken cancellationToken = default);

    Task<BankTransactionsPage> GetTransactionsAsync(
        string bankCode,
        string consentId,
        string resourceId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken = default);

    Task DeleteConsentAsync(
        string bankCode,
        string consentId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// O bancă din selectorul furnizorului.
/// </summary>
/// <param name="Id">Codul cu care banca se identifică în toate celelalte apeluri (`BT`, `BCR`, …).</param>
/// <param name="RequiresPsuId">Banca cere numele de utilizator de la ea înainte de consimțământ.</param>
/// <param name="RequiresPsuIdType">Banca cere să spunem dacă e cont de persoană fizică sau juridică.</param>
/// <param name="RequiresIban">Banca cere IBAN-ul dinainte, ca să știe pentru ce cont se dă acordul.</param>
public sealed record BankInstitutionInfo(
    string Id,
    string Name,
    string? Logo,
    string? Bic = null,
    bool Active = true,
    bool RequiresPsuId = false,
    bool RequiresPsuIdType = false,
    bool RequiresIban = false);

/// <param name="PsuReference">Identificatorul utilizatorului la noi, trimis furnizorului ca să-și lege consimțământul de cineva.</param>
/// <param name="RedirectAddress">Unde întoarce banca utilizatorul după autorizare.</param>
/// <param name="PsuIpAddress">
/// IP-ul utilizatorului. Fără el furnizorul limitează integrarea la 4 apeluri pe zi, deci se
/// trimite la tot ce pornește dintr-un click; la sincronizarea din fundal nu avem ce trimite.
/// </param>
/// <param name="PsuIdType">„PF" sau „PJ", la băncile care fac diferența.</param>
/// <param name="PsuCorporateId">Codul de client firmă, la băncile care cer și utilizatorul, și firma.</param>
public sealed record BankConsentRequest(
    string BankCode,
    string PsuEmail,
    string PsuReference,
    string RedirectAddress,
    string? PsuIpAddress = null,
    string? PsuId = null,
    string? PsuIdType = null,
    string? PsuCorporateId = null,
    string? Iban = null,
    int ValidDays = 180);

public sealed record BankConsentCreated(
    string ConsentId,
    string AuthorizationAddress,
    string? ConsentStatus,
    BankConsentTokens Tokens);

/// <param name="ValidUntilUtc">Până când ține acordul dat la bancă; după, utilizatorul reautorizează.</param>
public sealed record BankConsentState(
    string? Status,
    DateTime? ValidUntilUtc);

/// <summary>
/// Perechea de tokenuri a unui consimțământ.
///
/// Cel de acces trăiește 5 minute, cel de reîmprospătare 90 de zile și se rotește: emiterea unuia
/// nou îl invalidează pe cel dinainte. De aceea nu se ține în memorie, ci se salvează la fiecare
/// reînnoire — un proces repornit între două apeluri ar rămâne altfel cu o pereche moartă.
/// </summary>
public sealed record BankConsentTokens(
    string AccessToken,
    string RefreshToken,
    DateTime AccessExpiresAtUtc);

/// <param name="ResourceId">Cum numește banca acest cont în apelurile următoare.</param>
public sealed record BankAccountDetailsInfo(
    string ResourceId,
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
