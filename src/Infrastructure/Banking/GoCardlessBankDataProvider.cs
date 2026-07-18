using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions.Services;
using Microsoft.Extensions.Options;

namespace Infrastructure.Banking;

/// <summary>
/// GoCardless Bank Account Data (fostul Nordigen) — client PSD2 AIS.
/// Implementează IBankDataProvider; restul sistemului nu vede tipuri GoCardless,
/// ca providerul să poată fi înlocuit fără modificări în handlers/jobs/endpoints.
/// </summary>
internal sealed class GoCardlessBankDataProvider(
    HttpClient httpClient,
    IOptions<GoCardlessOptions> optionsAccessor) : IBankDataProvider
{
    public const string SandboxInstitutionId = "SANDBOXFINANCE_SFIN0000";

    private static readonly string[] FullAccessScope = ["balances", "details", "transactions"];

    private readonly GoCardlessOptions _options = optionsAccessor.Value;

    public string ProviderName => "GoCardless";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.SecretId) &&
        !string.IsNullOrWhiteSpace(_options.SecretKey);

    public async Task<IReadOnlyList<BankInstitutionInfo>> ListInstitutionsAsync(
        string countryCode,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        string token = await GetAccessTokenAsync(cancellationToken);

        JsonElement list = await SendAsync(
            HttpMethod.Get,
            $"institutions/?country={Uri.EscapeDataString(countryCode)}",
            body: null,
            token,
            cancellationToken);

        var institutions = new List<BankInstitutionInfo>();

        if (_options.UseSandboxInstitution)
        {
            institutions.Add(new BankInstitutionInfo(
                SandboxInstitutionId,
                "Sandbox Finance (test)",
                null,
                MaxHistoricalDays: 90));
        }

        foreach (JsonElement item in list.EnumerateArray())
        {
            string? id = GetString(item, "id");
            string? name = GetString(item, "name");
            if (id is null || name is null || id == SandboxInstitutionId)
            {
                continue;
            }

            int historyDays = 90;
            if (item.TryGetProperty("transaction_total_days", out JsonElement daysEl))
            {
                _ = int.TryParse(daysEl.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out historyDays);
            }

            institutions.Add(new BankInstitutionInfo(id, name, GetString(item, "logo"), historyDays));
        }

        return institutions;
    }

    public async Task<BankRequisitionCreated> CreateRequisitionAsync(
        string institutionId,
        string redirectAddress,
        string reference,
        int maxHistoricalDays,
        int accessValidForDays,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        string token = await GetAccessTokenAsync(cancellationToken);

        // Agreement explicit doar când cerem altceva decât default-urile (90/90).
        string? agreementId = null;
        if (maxHistoricalDays != 90 || accessValidForDays != 90)
        {
            JsonElement agreement = await SendAsync(
                HttpMethod.Post,
                "agreements/enduser/",
                new Dictionary<string, object?>
                {
                    ["institution_id"] = institutionId,
                    ["max_historical_days"] = maxHistoricalDays,
                    ["access_valid_for_days"] = accessValidForDays,
                    ["access_scope"] = FullAccessScope,
                },
                token,
                cancellationToken);

            agreementId = GetString(agreement, "id");
        }

        var body = new Dictionary<string, object?>
        {
            ["redirect"] = redirectAddress,
            ["institution_id"] = institutionId,
            ["reference"] = reference,
            ["user_language"] = "RO",
        };
        if (agreementId is not null)
        {
            body["agreement"] = agreementId;
        }

        JsonElement requisition = await SendAsync(
            HttpMethod.Post,
            "requisitions/",
            body,
            token,
            cancellationToken);

        string? requisitionId = GetString(requisition, "id");
        string? link = GetString(requisition, "link");
        if (requisitionId is null || link is null)
        {
            throw new BankDataProviderException("Răspunsul GoCardless la crearea requisition-ului nu conține id/link.");
        }

        return new BankRequisitionCreated(requisitionId, agreementId, link);
    }

    public async Task<BankRequisitionDetails> GetRequisitionAsync(
        string requisitionId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        string token = await GetAccessTokenAsync(cancellationToken);

        JsonElement requisition = await SendAsync(
            HttpMethod.Get,
            $"requisitions/{Uri.EscapeDataString(requisitionId)}/",
            body: null,
            token,
            cancellationToken);

        var accountIds = new List<string>();
        if (requisition.TryGetProperty("accounts", out JsonElement accountsEl) &&
            accountsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement account in accountsEl.EnumerateArray())
            {
                string? id = account.GetString();
                if (!string.IsNullOrEmpty(id))
                {
                    accountIds.Add(id);
                }
            }
        }

        BankRequisitionStatus status = GetString(requisition, "status") switch
        {
            "CR" => BankRequisitionStatus.Created,
            "GC" => BankRequisitionStatus.GivingConsent,
            "UA" or "GA" or "SA" => BankRequisitionStatus.UndergoingAuthentication,
            "LN" => BankRequisitionStatus.Linked,
            "EX" => BankRequisitionStatus.Expired,
            "RJ" => BankRequisitionStatus.Rejected,
            "SU" => BankRequisitionStatus.Suspended,
            _ => BankRequisitionStatus.Created,
        };

        // Consimțământul e definit pe agreement; requisition-ul nu îl expune direct.
        DateTime? consentExpiresAtUtc = null;
        string? agreementId = GetString(requisition, "agreement");
        if (agreementId is not null && status == BankRequisitionStatus.Linked)
        {
            try
            {
                JsonElement agreement = await SendAsync(
                    HttpMethod.Get,
                    $"agreements/enduser/{Uri.EscapeDataString(agreementId)}/",
                    body: null,
                    token,
                    cancellationToken);

                string? accepted = GetString(agreement, "accepted");
                int validDays = 90;
                if (agreement.TryGetProperty("access_valid_for_days", out JsonElement validEl))
                {
                    _ = validEl.TryGetInt32(out validDays);
                }

                DateTime acceptedUtc = accepted is not null &&
                    DateTime.TryParse(accepted, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)
                    ? parsed
                    : DateTime.UtcNow;

                consentExpiresAtUtc = acceptedUtc.AddDays(validDays);
            }
            catch (BankDataProviderException)
            {
                // Nu blocăm linkarea dacă agreementul nu poate fi citit; folosim default-ul.
                consentExpiresAtUtc = DateTime.UtcNow.AddDays(90);
            }
        }

        return new BankRequisitionDetails(status, accountIds, consentExpiresAtUtc);
    }

    public async Task DeleteRequisitionAsync(
        string requisitionId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        string token = await GetAccessTokenAsync(cancellationToken);

        await SendAsync(
            HttpMethod.Delete,
            $"requisitions/{Uri.EscapeDataString(requisitionId)}/",
            body: null,
            token,
            cancellationToken,
            expectBody: false);
    }

    public async Task<BankAccountDetailsInfo> GetAccountDetailsAsync(
        string providerAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        string token = await GetAccessTokenAsync(cancellationToken);

        // Metadata endpoint — nu consumă din rate limit-ul băncii pentru details.
        JsonElement meta = await SendAsync(
            HttpMethod.Get,
            $"accounts/{Uri.EscapeDataString(providerAccountId)}/",
            body: null,
            token,
            cancellationToken);

        string? iban = GetString(meta, "iban");
        string? ownerName = GetString(meta, "owner_name");
        string? currency = null;

        try
        {
            JsonElement details = await SendAsync(
                HttpMethod.Get,
                $"accounts/{Uri.EscapeDataString(providerAccountId)}/details/",
                body: null,
                token,
                cancellationToken);

            if (details.TryGetProperty("account", out JsonElement accountEl))
            {
                iban ??= GetString(accountEl, "iban");
                currency = GetString(accountEl, "currency");
                ownerName ??= GetString(accountEl, "ownerName");
            }
        }
        catch (BankDataRateLimitException)
        {
            // Detaliile sunt nice-to-have; nu blocăm linkarea pe rate limit.
        }

        return new BankAccountDetailsInfo(iban, currency, ownerName);
    }

    public async Task<BankTransactionsPage> GetTransactionsAsync(
        string providerAccountId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        string token = await GetAccessTokenAsync(cancellationToken);

        string path = $"accounts/{Uri.EscapeDataString(providerAccountId)}/transactions/";
        var query = new List<string>();
        if (dateFrom is not null)
        {
            query.Add($"date_from={dateFrom.Value:yyyy-MM-dd}");
        }
        if (dateTo is not null)
        {
            query.Add($"date_to={dateTo.Value:yyyy-MM-dd}");
        }
        if (query.Count > 0)
        {
            path += "?" + string.Join("&", query);
        }

        JsonElement payload = await SendAsync(HttpMethod.Get, path, body: null, token, cancellationToken);

        if (!payload.TryGetProperty("transactions", out JsonElement transactionsEl))
        {
            throw new BankDataProviderException("Răspunsul GoCardless la tranzacții nu conține câmpul \"transactions\".");
        }

        return new BankTransactionsPage(
            ParseTransactions(transactionsEl, "booked"),
            ParseTransactions(transactionsEl, "pending"));
    }

    // ── Parsing helpers ──────────────────────────────────────────────────────

    private static List<BankTransactionInfo> ParseTransactions(JsonElement transactionsEl, string key)
    {
        var result = new List<BankTransactionInfo>();
        if (!transactionsEl.TryGetProperty(key, out JsonElement listEl) ||
            listEl.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement tx in listEl.EnumerateArray())
        {
            decimal amount = 0;
            string currency = string.Empty;
            if (tx.TryGetProperty("transactionAmount", out JsonElement amountEl))
            {
                _ = decimal.TryParse(GetString(amountEl, "amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
                currency = GetString(amountEl, "currency") ?? string.Empty;
            }

            string? providerTransactionId =
                GetString(tx, "internalTransactionId") ?? GetString(tx, "transactionId");

            string rawJson = tx.GetRawText();

            // Fallback determinist când banca nu trimite niciun id.
            providerTransactionId ??= Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawJson)));

            string? counterparty = amount >= 0
                ? GetString(tx, "debtorName") ?? GetString(tx, "creditorName")
                : GetString(tx, "creditorName") ?? GetString(tx, "debtorName");

            string? remittance = GetString(tx, "remittanceInformationUnstructured");
            if (remittance is null &&
                tx.TryGetProperty("remittanceInformationUnstructuredArray", out JsonElement remArr) &&
                remArr.ValueKind == JsonValueKind.Array)
            {
                remittance = string.Join(" ", remArr.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
            }

            result.Add(new BankTransactionInfo(
                providerTransactionId,
                ParseDate(GetString(tx, "bookingDate")),
                ParseDate(GetString(tx, "valueDate")),
                amount,
                currency,
                counterparty,
                remittance,
                rawJson));
        }

        return result;
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, out DateOnly date) ? date : null;

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out JsonElement el) &&
        el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    // ── HTTP helpers ─────────────────────────────────────────────────────────

    private Task<string> GetAccessTokenAsync(CancellationToken ct) =>
        TokenCache.GetAsync(httpClient, BuildUri("token/new/"), _options, ct);

    /// <summary>Process-wide access-token cache (tokenul e valabil ~24h).</summary>
    private static class TokenCache
    {
        private static readonly SemaphoreSlim Lock = new(1, 1);
        private static string? _token;
        private static DateTime _expiresUtc;

        public static async Task<string> GetAsync(
            HttpClient httpClient,
            Uri tokenUri,
            GoCardlessOptions options,
            CancellationToken ct)
        {
            if (_token is not null && DateTime.UtcNow < _expiresUtc)
            {
                return _token;
            }

            await Lock.WaitAsync(ct);
            try
            {
                if (_token is not null && DateTime.UtcNow < _expiresUtc)
                {
                    return _token;
                }

                HttpResponseMessage response;
                try
                {
                    response = await httpClient.PostAsJsonAsync(tokenUri, new
                    {
                        secret_id = options.SecretId,
                        secret_key = options.SecretKey,
                    }, ct);
                }
                catch (HttpRequestException ex)
                {
                    throw new BankDataProviderException("Nu s-a putut contacta API-ul GoCardless.", ex);
                }

                string payload = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    throw new BankDataProviderException(
                        $"Autentificarea GoCardless a eșuat ({(int)response.StatusCode}): {ExtractErrorMessage(payload)}");
                }

                using var doc = JsonDocument.Parse(payload);
                string? token = doc.RootElement.TryGetProperty("access", out JsonElement tokenEl)
                    ? tokenEl.GetString()
                    : null;

                if (string.IsNullOrEmpty(token))
                {
                    throw new BankDataProviderException("Răspunsul de autentificare GoCardless nu conține access token.");
                }

                int expiresIn = 86400;
                if (doc.RootElement.TryGetProperty("access_expires", out JsonElement expEl))
                {
                    _ = expEl.TryGetInt32(out expiresIn);
                }

                _token = token;
                _expiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn - 300));
                return token;
            }
            finally
            {
                Lock.Release();
            }
        }
    }

    private async Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, object?>? body,
        string token,
        CancellationToken ct,
        bool expectBody = true)
    {
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new BankDataProviderException("Nu s-a putut contacta API-ul GoCardless.", ex);
        }

        string payload = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new BankDataRateLimitException(
                "Limita de apeluri către bancă a fost atinsă. Sincronizarea va fi reluată automat.",
                ExtractRetryAfter(response, payload));
        }

        bool consentExpired =
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized &&
            (payload.Contains("EUA", StringComparison.OrdinalIgnoreCase) ||
             payload.Contains("expired", StringComparison.OrdinalIgnoreCase));
        if (consentExpired)
        {
            throw new BankDataConsentExpiredException(
                "Consimțământul acordat băncii a expirat. Reconectează contul bancar.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BankDataProviderException(
                $"Cererea către GoCardless a eșuat ({(int)response.StatusCode}): {ExtractErrorMessage(payload)}");
        }

        if (!expectBody || string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        using var doc = JsonDocument.Parse(payload);

        // Listele paginate vin ca {count, next, previous, results: [...]}.
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("results", out JsonElement results))
        {
            return results.Clone();
        }

        return doc.RootElement.Clone();
    }

    private static TimeSpan? ExtractRetryAfter(HttpResponseMessage response, string payload)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
        {
            return delta;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            // GoCardless trimite "...try again in N seconds" în detail.
            string? detail = doc.RootElement.TryGetProperty("detail", out JsonElement detailEl)
                ? detailEl.GetString()
                : null;
            if (detail is not null)
            {
                string digits = new([.. detail.Where(char.IsDigit)]);
                if (int.TryParse(digits, out int seconds) && seconds > 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return null;
    }

    private Uri BuildUri(string path) =>
        new($"{_options.BaseUrl.TrimEnd('/')}/{path}");

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new BankDataProviderException(
                "Integrarea GoCardless nu este configurată. Setează GoCardless:SecretId și GoCardless:SecretKey.");
        }
    }

    private static string ExtractErrorMessage(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (string key in (string[])["detail", "summary", "reference"])
                {
                    if (doc.RootElement.TryGetProperty(key, out JsonElement el) &&
                        el.ValueKind == JsonValueKind.String)
                    {
                        return el.GetString() ?? payload;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // fall through — return raw payload
        }

        return payload.Length > 500 ? payload[..500] : payload;
    }
}
