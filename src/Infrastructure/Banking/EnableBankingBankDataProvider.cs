using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Services;
using Microsoft.Extensions.Options;

namespace Infrastructure.Banking;

/// <summary>
/// Enable Banking (api.enablebanking.com) — client PSD2 AIS.
/// Implementează IBankDataProvider; restul sistemului nu vede tipuri Enable Banking.
/// Particularități față de GoCardless: autentificare cu JWT semnat RS256 (nu secret exchange),
/// instituțiile sunt identificate prin (țară, nume) — codificate aici ca "ȚARĂ:Nume" —
/// iar redirectul de la bancă întoarce un cod one-time care se schimbă pe o sesiune.
/// </summary>
internal sealed class EnableBankingBankDataProvider(
    HttpClient httpClient,
    IOptions<EnableBankingOptions> optionsAccessor) : IBankDataProvider
{
    public const string SandboxInstitutionName = "Mock ASPSP";

    /// <summary>
    /// PSD2 nu garantează istoric peste 90 de zile fără SCA suplimentar,
    /// iar Enable Banking nu expune limita per bancă — folosim default-ul sigur.
    /// </summary>
    private const int DefaultHistoricalDays = 90;

    private readonly EnableBankingOptions _options = optionsAccessor.Value;

    private (string ApplicationId, string PrivateKeyPem)? _credentials;
    private bool _credentialsResolved;

    public string ProviderName => "EnableBanking";

    public bool IsConfigured => ResolveCredentials() is not null;

    /// <summary>
    /// Credențialele vin din config (ApplicationId + PrivateKeyPem) sau, în lipsă,
    /// din fișierul .pem găsit lângă aplicație — al cărui nume (convenția Enable Banking)
    /// este chiar Application ID-ul.
    /// </summary>
    private (string ApplicationId, string PrivateKeyPem)? ResolveCredentials()
    {
        if (_credentialsResolved)
        {
            return _credentials;
        }
        _credentialsResolved = true;

        string? applicationId = string.IsNullOrWhiteSpace(_options.ApplicationId)
            ? null
            : _options.ApplicationId.Trim();
        string? privateKeyPem = string.IsNullOrWhiteSpace(_options.PrivateKeyPem)
            ? null
            : _options.PrivateKeyPem;

        if (privateKeyPem is null)
        {
            string? path = string.IsNullOrWhiteSpace(_options.PrivateKeyPath)
                ? EnableBankingKeyFile.Find()
                : ResolveKeyPath(_options.PrivateKeyPath.Trim());

            if (path is not null && File.Exists(path))
            {
                privateKeyPem = File.ReadAllText(path);
                applicationId ??= EnableBankingKeyFile.ApplicationIdFromFileName(path);
            }
        }

        _credentials = applicationId is not null && privateKeyPem is not null
            ? (applicationId, privateKeyPem)
            : null;
        return _credentials;
    }

    private static string ResolveKeyPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        string besideApp = Path.Combine(AppContext.BaseDirectory, path);
        return File.Exists(besideApp) ? besideApp : Path.GetFullPath(path);
    }

    public async Task<IReadOnlyList<BankInstitutionInfo>> ListInstitutionsAsync(
        string countryCode,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        string country = countryCode.ToUpperInvariant();

        JsonElement payload = await SendAsync(
            HttpMethod.Get,
            $"aspsps?country={Uri.EscapeDataString(country)}",
            body: null,
            cancellationToken);

        var institutions = new List<BankInstitutionInfo>();

        if (payload.TryGetProperty("aspsps", out JsonElement listEl) &&
            listEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in listEl.EnumerateArray())
            {
                string? name = GetString(item, "name");
                string? itemCountry = GetString(item, "country") ?? country;
                if (name is null)
                {
                    continue;
                }

                institutions.Add(new BankInstitutionInfo(
                    EncodeInstitutionId(itemCountry, name),
                    name,
                    GetString(item, "logo"),
                    DefaultHistoricalDays));
            }
        }

        if (_options.UseSandboxInstitution &&
            !institutions.Any(i => DecodeInstitutionId(i.Id).Name == SandboxInstitutionName))
        {
            institutions.Insert(0, new BankInstitutionInfo(
                EncodeInstitutionId(country, SandboxInstitutionName),
                "Mock ASPSP (test)",
                null,
                DefaultHistoricalDays));
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
        (string country, string name) = DecodeInstitutionId(institutionId);

        // Consimțământul nu poate depăși maximum_consent_validity al băncii (în secunde).
        long requestedSeconds = (long)accessValidForDays * 86400;
        long validitySeconds = Math.Min(
            requestedSeconds,
            await GetMaxConsentValiditySecondsAsync(country, name, requestedSeconds, cancellationToken));

        string validUntil = DateTime.UtcNow
            .AddSeconds(validitySeconds)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        JsonElement auth = await SendAsync(
            HttpMethod.Post,
            "auth",
            new Dictionary<string, object?>
            {
                ["access"] = new Dictionary<string, object?> { ["valid_until"] = validUntil },
                ["aspsp"] = new Dictionary<string, object?> { ["name"] = name, ["country"] = country },
                ["state"] = reference,
                ["redirect_url"] = redirectAddress,
                ["psu_type"] = _options.PsuType,
            },
            cancellationToken);

        string? authorizationId = GetString(auth, "authorization_id");
        string? link = GetString(auth, "url");
        if (authorizationId is null || link is null)
        {
            throw new BankDataProviderException(
                "Răspunsul Enable Banking la pornirea autorizării nu conține authorization_id/url.");
        }

        return new BankRequisitionCreated(authorizationId, AgreementId: null, link);
    }

    public async Task<BankRequisitionDetails> GetRequisitionAsync(
        string requisitionId,
        string? authorizationCode = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Cu cod de la redirect: schimbăm codul pe o sesiune — abia acum avem acces la conturi.
        if (!string.IsNullOrEmpty(authorizationCode))
        {
            JsonElement session = await SendAsync(
                HttpMethod.Post,
                "sessions",
                new Dictionary<string, object?> { ["code"] = authorizationCode },
                cancellationToken);

            string sessionId = GetString(session, "session_id")
                ?? throw new BankDataProviderException(
                    "Răspunsul Enable Banking la crearea sesiunii nu conține session_id.");

            return new BankRequisitionDetails(
                BankRequisitionStatus.Linked,
                ParseAccountIds(session),
                ParseConsentExpiry(session),
                UpdatedRequisitionId: sessionId);
        }

        // Fără cod: id-ul stocat e fie o sesiune existentă, fie un authorization_id
        // pentru care userul nu a terminat autorizarea (sesiunea nu există încă → 404).
        JsonElement? existing = await SendAsync(
            HttpMethod.Get,
            $"sessions/{Uri.EscapeDataString(requisitionId)}",
            body: null,
            allowNotFound: true,
            expectBody: true,
            cancellationToken);

        if (existing is null)
        {
            return new BankRequisitionDetails(BankRequisitionStatus.Created, [], null);
        }

        BankRequisitionStatus status = GetString(existing.Value, "status") switch
        {
            "AUTHORIZED" => BankRequisitionStatus.Linked,
            "EXPIRED" or "CLOSED" or "REVOKED" => BankRequisitionStatus.Expired,
            "CANCELLED" or "REJECTED" => BankRequisitionStatus.Rejected,
            _ => BankRequisitionStatus.Created,
        };

        return new BankRequisitionDetails(status, ParseAccountIds(existing.Value), ParseConsentExpiry(existing.Value));
    }

    public async Task DeleteRequisitionAsync(
        string requisitionId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Închide sesiunea și revocă consimțământul la bancă. Un authorization_id
        // neschimbat pe sesiune nu are ce șterge — 404 e ignorat.
        await SendAsync(
            HttpMethod.Delete,
            $"sessions/{Uri.EscapeDataString(requisitionId)}",
            body: null,
            allowNotFound: true,
            expectBody: false,
            cancellationToken);
    }

    public async Task<BankAccountDetailsInfo> GetAccountDetailsAsync(
        string providerAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        JsonElement details = await SendAsync(
            HttpMethod.Get,
            $"accounts/{Uri.EscapeDataString(providerAccountId)}/details",
            body: null,
            cancellationToken);

        string? iban = details.TryGetProperty("account_id", out JsonElement accountIdEl)
            ? GetString(accountIdEl, "iban")
            : null;

        return new BankAccountDetailsInfo(
            iban,
            GetString(details, "currency"),
            GetString(details, "name"));
    }

    public async Task<BankTransactionsPage> GetTransactionsAsync(
        string providerAccountId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var booked = new List<BankTransactionInfo>();
        var pending = new List<BankTransactionInfo>();

        string? continuationKey = null;
        const int maxPages = 50;

        for (int pageIndex = 0; pageIndex < maxPages; pageIndex++)
        {
            var query = new List<string>();
            if (dateFrom is not null)
            {
                query.Add($"date_from={dateFrom.Value:yyyy-MM-dd}");
            }
            if (dateTo is not null)
            {
                query.Add($"date_to={dateTo.Value:yyyy-MM-dd}");
            }
            if (continuationKey is not null)
            {
                query.Add($"continuation_key={Uri.EscapeDataString(continuationKey)}");
            }

            string path = $"accounts/{Uri.EscapeDataString(providerAccountId)}/transactions";
            if (query.Count > 0)
            {
                path += "?" + string.Join("&", query);
            }

            JsonElement payload = await SendAsync(HttpMethod.Get, path, body: null, cancellationToken);

            if (payload.TryGetProperty("transactions", out JsonElement listEl) &&
                listEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement tx in listEl.EnumerateArray())
                {
                    string? status = GetString(tx, "status");
                    if (status == "BOOK")
                    {
                        booked.Add(ParseTransaction(tx));
                    }
                    else if (status == "PDNG")
                    {
                        pending.Add(ParseTransaction(tx));
                    }
                    // "INFO" și alte statusuri informative nu sunt tranzacții reale.
                }
            }

            continuationKey = GetString(payload, "continuation_key");
            if (string.IsNullOrEmpty(continuationKey))
            {
                break;
            }
        }

        return new BankTransactionsPage(booked, pending);
    }

    // ── Parsing helpers ──────────────────────────────────────────────────────

    private static BankTransactionInfo ParseTransaction(JsonElement tx)
    {
        decimal amount = 0;
        string currency = string.Empty;
        if (tx.TryGetProperty("transaction_amount", out JsonElement amountEl))
        {
            _ = decimal.TryParse(GetString(amountEl, "amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
            currency = GetString(amountEl, "currency") ?? string.Empty;
        }

        // Semnul vine din credit_debit_indicator; suma e raportată de regulă pozitivă.
        string? indicator = GetString(tx, "credit_debit_indicator");
        if (indicator == "DBIT")
        {
            amount = -Math.Abs(amount);
        }
        else if (indicator == "CRDT")
        {
            amount = Math.Abs(amount);
        }

        string rawJson = tx.GetRawText();

        string? providerTransactionId =
            GetString(tx, "entry_reference") ?? GetString(tx, "transaction_id");
        providerTransactionId ??= Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)));

        string? counterparty = amount >= 0
            ? GetNestedName(tx, "debtor") ?? GetNestedName(tx, "creditor")
            : GetNestedName(tx, "creditor") ?? GetNestedName(tx, "debtor");

        string? remittance = null;
        if (tx.TryGetProperty("remittance_information", out JsonElement remEl))
        {
            remittance = remEl.ValueKind switch
            {
                JsonValueKind.Array => string.Join(" ", remEl.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
                JsonValueKind.String => remEl.GetString(),
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(remittance))
            {
                remittance = null;
            }
        }

        return new BankTransactionInfo(
            providerTransactionId,
            ParseDate(GetString(tx, "booking_date")),
            ParseDate(GetString(tx, "value_date")),
            amount,
            currency,
            counterparty,
            remittance,
            rawJson);
    }

    private static List<string> ParseAccountIds(JsonElement session)
    {
        var accountIds = new List<string>();
        if (!session.TryGetProperty("accounts", out JsonElement accountsEl) ||
            accountsEl.ValueKind != JsonValueKind.Array)
        {
            return accountIds;
        }

        // POST /sessions întoarce obiecte cu "uid"; GET /sessions/{id} întoarce direct uid-uri.
        foreach (JsonElement account in accountsEl.EnumerateArray())
        {
            string? id = account.ValueKind switch
            {
                JsonValueKind.String => account.GetString(),
                JsonValueKind.Object => GetString(account, "uid"),
                _ => null,
            };
            if (!string.IsNullOrEmpty(id))
            {
                accountIds.Add(id);
            }
        }

        return accountIds;
    }

    private static DateTime? ParseConsentExpiry(JsonElement session)
    {
        string? validUntil = session.TryGetProperty("access", out JsonElement accessEl)
            ? GetString(accessEl, "valid_until")
            : null;

        return validUntil is not null &&
            DateTime.TryParse(validUntil, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)
            ? parsed
            : null;
    }

    private static string EncodeInstitutionId(string country, string name) =>
        $"{country.ToUpperInvariant()}:{name}";

    private static (string Country, string Name) DecodeInstitutionId(string institutionId)
    {
        int separator = institutionId.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == institutionId.Length - 1)
        {
            throw new BankDataProviderException($"Identificator de bancă invalid: \"{institutionId}\".");
        }

        return (institutionId[..separator], institutionId[(separator + 1)..]);
    }

    private async Task<long> GetMaxConsentValiditySecondsAsync(
        string country,
        string name,
        long fallbackSeconds,
        CancellationToken ct)
    {
        try
        {
            JsonElement payload = await SendAsync(
                HttpMethod.Get,
                $"aspsps?country={Uri.EscapeDataString(country)}",
                body: null,
                ct);

            if (payload.TryGetProperty("aspsps", out JsonElement listEl) &&
                listEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in listEl.EnumerateArray())
                {
                    if (GetString(item, "name") == name &&
                        item.TryGetProperty("maximum_consent_validity", out JsonElement maxEl) &&
                        maxEl.TryGetInt64(out long seconds) &&
                        seconds > 0)
                    {
                        return seconds;
                    }
                }
            }
        }
        catch (BankDataProviderException)
        {
            // Limita e doar o optimizare — banca oricum respinge o validitate prea mare.
        }

        return fallbackSeconds;
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, out DateOnly date) ? date : null;

    private static string? GetNestedName(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement nested) && nested.ValueKind == JsonValueKind.Object
            ? GetString(nested, "name")
            : null;

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out JsonElement el) &&
        el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    // ── Auth (JWT RS256 semnat cu cheia privată a aplicației) ────────────────

    /// <summary>Cache pe proces al JWT-ului semnat (regenerat cu 5 min înainte de expirare).</summary>
    private static class JwtCache
    {
        private static readonly Lock Sync = new();
        private static string? _token;
        private static DateTime _expiresUtc;

        public static string Get(string applicationId, string privateKeyPem)
        {
            lock (Sync)
            {
                if (_token is not null && DateTime.UtcNow < _expiresUtc)
                {
                    return _token;
                }

                const int lifetimeSeconds = 3600;
                _token = Create(applicationId, privateKeyPem, lifetimeSeconds);
                _expiresUtc = DateTime.UtcNow.AddSeconds(lifetimeSeconds - 300);
                return _token;
            }
        }

        private static string Create(string applicationId, string privateKeyPem, int lifetimeSeconds)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            string header = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["typ"] = "JWT",
                ["alg"] = "RS256",
                ["kid"] = applicationId,
            });
            string payload = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["iss"] = "enablebanking.com",
                ["aud"] = "api.enablebanking.com",
                ["iat"] = now,
                ["exp"] = now + lifetimeSeconds,
            });

            string signingInput = $"{Base64Url(Encoding.UTF8.GetBytes(header))}.{Base64Url(Encoding.UTF8.GetBytes(payload))}";

            using var rsa = RSA.Create();
            try
            {
                rsa.ImportFromPem(NormalizePrivateKey(privateKeyPem));
            }
            catch (ArgumentException ex)
            {
                throw new BankDataProviderException(
                    "Cheia privată Enable Banking nu este un PEM valid (acceptă PEM brut sau base64).", ex);
            }

            byte[] signature = rsa.SignData(
                Encoding.UTF8.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            return $"{signingInput}.{Base64Url(signature)}";
        }

        private static string NormalizePrivateKey(string raw)
        {
            string value = raw.Trim();

            if (!value.Contains("BEGIN", StringComparison.Ordinal))
            {
                try
                {
                    value = Encoding.UTF8.GetString(Convert.FromBase64String(value)).Trim();
                }
                catch (FormatException)
                {
                    // Rămâne valoarea brută — ImportFromPem va da eroarea finală.
                }
            }

            // Env-urile pe o singură linie ajung cu "\n" escapat.
            return value.Replace("\\n", "\n", StringComparison.Ordinal);
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // ── HTTP helpers ─────────────────────────────────────────────────────────

    private async Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, object?>? body,
        CancellationToken ct)
    {
        JsonElement? result = await SendAsync(method, path, body, allowNotFound: false, expectBody: true, ct);
        return result ?? default;
    }

    private async Task<JsonElement?> SendAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, object?>? body,
        bool allowNotFound,
        bool expectBody,
        CancellationToken ct)
    {
        (string applicationId, string privateKeyPem) = ResolveCredentials()
            ?? throw new BankDataProviderException("Integrarea Enable Banking nu este configurată.");

        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", JwtCache.Get(applicationId, privateKeyPem));
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
            throw new BankDataProviderException("Nu s-a putut contacta API-ul Enable Banking.", ex);
        }

        string payload = await response.Content.ReadAsStringAsync(ct);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new BankDataRateLimitException(
                "Limita de apeluri către bancă a fost atinsă. Sincronizarea va fi reluată automat.",
                response.Headers.RetryAfter?.Delta);
        }

        bool consentExpired =
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized &&
            (payload.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
             payload.Contains("revoked", StringComparison.OrdinalIgnoreCase) ||
             payload.Contains("closed", StringComparison.OrdinalIgnoreCase));
        if (consentExpired)
        {
            throw new BankDataConsentExpiredException(
                "Consimțământul acordat băncii a expirat. Reconectează contul bancar.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BankDataProviderException(
                $"Cererea către Enable Banking a eșuat ({(int)response.StatusCode}): {ExtractErrorMessage(payload)}");
        }

        if (!expectBody || string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.Clone();
    }

    private Uri BuildUri(string path) =>
        new($"{_options.BaseUrl.TrimEnd('/')}/{path}");

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new BankDataProviderException(
                "Integrarea Enable Banking nu este configurată. Pune fișierul .pem al aplicației lângă Web.Api " +
                "sau setează EnableBanking:ApplicationId și EnableBanking:PrivateKeyPem.");
        }
    }

    private static string ExtractErrorMessage(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (string key in (string[])["error_description", "message", "detail", "error"])
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
