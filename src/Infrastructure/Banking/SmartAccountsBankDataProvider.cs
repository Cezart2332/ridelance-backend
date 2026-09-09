using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions.Services;
using Microsoft.Extensions.Options;

namespace Infrastructure.Banking;

/// <summary>
/// Smart Accounts (Smart Fintech) — furnizorul de open banking.
///
/// Fluxul e PSD2 clasic, în trei mișcări: cerem un token (peste mTLS, cu certificatul nostru de
/// partener), deschidem un consimțământ pentru banca aleasă și primim adresa la care trimitem
/// utilizatorul, iar după ce el autorizează citim conturile și tranzacțiile cu același token.
///
/// Partea care nu se vede din interfață e ciclul tokenurilor: sunt emise per consimțământ, cel de
/// acces ține cinci minute, iar cel de reîmprospătare se rotește la fiecare folosire. De aceea
/// clasa asta nu ține nimic în memorie între apeluri — cere tokenul din
/// <see cref="IBankConsentTokenStore"/>, îl reînnoiește dacă e pe terminate și salvează imediat
/// perechea nouă.
/// </summary>
internal sealed class SmartAccountsBankDataProvider(
    HttpClient httpClient,
    IOptions<SmartAccountsOptions> optionsAccessor,
    SmartAccountsCertificate certificate,
    IBankConsentTokenStore tokenStore)
    : IBankDataProvider
{
    /// <summary>Cât ține un token de acces, după documentația furnizorului.</summary>
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Cu cât înainte de expirare reînnoim. Un token care mai are treizeci de secunde e bun pe
    /// hârtie și mort la jumătatea unei sincronizări de tranzacții.
    /// </summary>
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Reînnoirile aceluiași consimțământ se serializează: tokenul de reîmprospătare se rotește,
    /// deci două apeluri paralele s-ar invalida reciproc și consimțământul ar muri cu utilizatorul
    /// nevinovat. Cheia e consimțământul, nu procesul — bănci diferite nu se așteaptă între ele.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RefreshGates = new(StringComparer.Ordinal);

    /// <summary>
    /// Stările în care banca ne spune că acordul nu mai e bun. Restul („received", „valid",
    /// „partiallyAuthorised") înseamnă că fie mai așteptăm, fie merge.
    /// </summary>
    private static readonly string[] DeadConsentStatuses =
    [
        "expired",
        "revokedByPsu",
        "terminatedByTpp",
        "rejected",
    ];

    private readonly SmartAccountsOptions _options = optionsAccessor.Value;

    public string ProviderName => "SmartAccounts";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ClientId) && certificate.IsPresent;

    public async Task<IReadOnlyList<BankInstitutionInfo>> ListInstitutionsAsync(
        CancellationToken cancellationToken = default)
    {
        // Lista de bănci e publică în raport cu consimțământul: nu există încă unul, deci nici
        // token. Un consimțământ deschis doar ca să citim lista ar rămâne agățat, nefolosit.
        AuthSession session = await AuthenticateAsync(cancellationToken);

        JsonElement payload = SmartAccountsJson.Payload(await SendAsync(
            HttpMethod.Get,
            CoreUrl("banks"),
            session.Tokens.AccessToken,
            cancellationToken: cancellationToken));

        if (payload.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var banks = new List<BankInstitutionInfo>();

        foreach (JsonElement item in payload.EnumerateArray())
        {
            string? code = SmartAccountsJson.String(item, "code");
            if (code is null)
            {
                continue;
            }

            JsonElement required = item.TryGetProperty("requiredParameters", out JsonElement r) ? r : default;

            banks.Add(new BankInstitutionInfo(
                code,
                SmartAccountsJson.String(item, "name") ?? code,
                SmartAccountsJson.String(item, "logo"),
                SmartAccountsJson.String(item, "bic"),
                SmartAccountsJson.Bool(item, "active") ?? true,
                SmartAccountsJson.Bool(required, "requiresPSUId") ?? false,
                SmartAccountsJson.Bool(required, "requiresPSUIdType") ?? false,
                SmartAccountsJson.Bool(required, "requiresIban") ?? false));
        }

        return banks
            .Where(b => b.Active)
            .OrderBy(b => b.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task<BankConsentCreated> CreateConsentAsync(
        BankConsentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Autentificarea E deschiderea consimțământului: tokenurile vin împreună cu identificatorul
        // lui, iar amestecarea lor între consimțământuri e respinsă cu 401.
        AuthSession session = await AuthenticateAsync(cancellationToken);

        var body = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["codeBank"] = request.BankCode,
            ["periodOfValid"] = Math.Clamp(request.ValidDays, 90, 180),
            ["psuEmail"] = request.PsuEmail,
            ["psuIntermediarId"] = request.PsuReference,
            ["redirectURL"] = request.RedirectAddress,
            ["TCaccepted"] = true,
        };

        if (!string.IsNullOrWhiteSpace(request.Iban))
        {
            body["allAccounts"] = new[] { new { iban = request.Iban, currency = "RON" } };
        }

        var headers = new List<(string Name, string Value)>();

        if (!string.IsNullOrWhiteSpace(request.PsuIpAddress))
        {
            headers.Add(("PSU-IP-Address", request.PsuIpAddress));
        }

        if (!string.IsNullOrWhiteSpace(request.PsuId))
        {
            headers.Add(("PSU-ID", request.PsuId));
        }

        if (!string.IsNullOrWhiteSpace(request.PsuIdType))
        {
            headers.Add(("PSU-ID-Type", request.PsuIdType));
        }

        if (!string.IsNullOrWhiteSpace(request.PsuCorporateId))
        {
            headers.Add(("PSU-Corporate-ID", request.PsuCorporateId));
        }

        JsonElement payload = SmartAccountsJson.Payload(await SendAsync(
            HttpMethod.Post,
            CoreUrl($"initConsent/{Path(session.ConsentId)}"),
            session.Tokens.AccessToken,
            body,
            headers,
            cancellationToken: cancellationToken));

        string? href = null;

        if (payload.TryGetProperty("_links", out JsonElement links) &&
            links.TryGetProperty("scaOAuth", out JsonElement sca))
        {
            href = SmartAccountsJson.String(sca, "href");
        }

        if (string.IsNullOrWhiteSpace(href))
        {
            throw new BankDataProviderException(
                "Smart Accounts nu a întors adresa de autorizare a băncii.");
        }

        return new BankConsentCreated(
            session.ConsentId,
            href,
            SmartAccountsJson.String(payload, "consentStatus"),
            session.Tokens);
    }

    public async Task<BankConsentState> GetConsentAsync(
        string bankCode,
        string consentId,
        CancellationToken cancellationToken = default)
    {
        JsonElement payload = SmartAccountsJson.Payload(await SendWithConsentAsync(
            HttpMethod.Get,
            CoreUrl($"consent/{Path(bankCode)}/{Path(consentId)}"),
            consentId,
            cancellationToken: cancellationToken));

        return new BankConsentState(
            SmartAccountsJson.String(payload, "consentStatus"),
            SmartAccountsJson.Timestamp(payload, "validUntil"));
    }

    public async Task<IReadOnlyList<BankAccountDetailsInfo>> ListAccountsAsync(
        string bankCode,
        string consentId,
        CancellationToken cancellationToken = default)
    {
        JsonElement payload = SmartAccountsJson.Payload(await SendWithConsentAsync(
            HttpMethod.Get,
            CoreUrl($"accounts/{Path(bankCode)}/{Path(consentId)}"),
            consentId,
            cancellationToken: cancellationToken));

        if (payload.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var accounts = new List<BankAccountDetailsInfo>();

        foreach (JsonElement item in payload.EnumerateArray())
        {
            // Fără `resourceId` contul nu poate fi interogat mai departe, deci nu are ce căuta la
            // noi. La băncile care nu-l trimit, IBAN-ul chiar ăsta e — îl folosesc ca identificator.
            string? resourceId = SmartAccountsJson.String(item, "resourceId", "iban");
            if (resourceId is null)
            {
                continue;
            }

            accounts.Add(new BankAccountDetailsInfo(
                resourceId,
                SmartAccountsJson.String(item, "iban"),
                SmartAccountsJson.String(item, "currency"),
                SmartAccountsJson.String(item, "ownerName", "name")));
        }

        return accounts;
    }

    public async Task<BankTransactionsPage> GetTransactionsAsync(
        string bankCode,
        string consentId,
        string resourceId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken = default)
    {
        var booked = new List<BankTransactionInfo>();
        var pending = new List<BankTransactionInfo>();

        // ProCredit acceptă doar „booked"; restul băncilor înțeleg „both", adică și tranzacțiile
        // nefinalizate, care la un cont de firmă sunt jumătate din ce se vede în aplicația băncii.
        var query = new List<string>
        {
            $"bookingStatus={(bankCode.Equals("PCB", StringComparison.OrdinalIgnoreCase) ? "booked" : "both")}",
        };

        if (dateFrom is not null)
        {
            query.Add($"dateFrom={dateFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        if (dateTo is not null)
        {
            query.Add($"dateTo={dateTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        string url = CoreUrl(
            $"accounts/{Path(bankCode)}/{Path(resourceId)}/{Path(consentId)}/transactions?{string.Join('&', query)}");

        // Paginarea nu schimbă adresa: linkul întors se trimite înapoi ca antet `next`. Legătura e
        // făcută cu `dateFrom`/`dateTo`, deci intervalul trebuie să rămână identic între pagini.
        string? next = null;

        for (int page = 0; page < _options.MaxTransactionPages; page++)
        {
            JsonElement payload = SmartAccountsJson.Payload(await SendWithConsentAsync(
                HttpMethod.Get,
                url,
                consentId,
                headers: next is null ? null : [("next", next)],
                cancellationToken: cancellationToken));

            // Unele bănci întorc `{transactions: {booked, pending}}`, altele direct `{booked, pending}`.
            JsonElement page1 = payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("transactions", out JsonElement nested)
                ? nested
                : payload;

            Collect(page1, "booked", isPending: false, booked);
            Collect(page1, "pending", isPending: true, pending);

            next = SmartAccountsJson.NextPage(page1) ?? SmartAccountsJson.NextPage(payload);

            if (next is null)
            {
                break;
            }
        }

        return new BankTransactionsPage(booked, pending);
    }

    public async Task DeleteConsentAsync(
        string bankCode,
        string consentId,
        CancellationToken cancellationToken = default)
    {
        await SendWithConsentAsync(
            HttpMethod.Delete,
            CoreUrl($"deleteConsent/{Path(bankCode)}/{Path(consentId)}"),
            consentId,
            expectBody: false,
            tolerateNotFound: true,
            cancellationToken: cancellationToken);
    }

    // ── Tokenuri ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ce întoarce autentificarea: un consimțământ gol și tokenurile care îl deschid.
    ///
    /// Furnizorul le emite împreună, iar tokenurile sunt bune exclusiv pentru consimțământul ăsta
    /// — folosite pe altul, răspunde 401.
    /// </summary>
    private sealed record AuthSession(string ConsentId, BankConsentTokens Tokens);

    private async Task<AuthSession> AuthenticateAsync(CancellationToken cancellationToken)
    {
        JsonElement payload = SmartAccountsJson.Payload(await SendAsync(
            HttpMethod.Post,
            $"{_options.MtlsBaseUrl.TrimEnd('/')}/gateway/authenticate/rest/api/token" +
            $"?client_id={Uri.EscapeDataString(_options.ClientId)}",
            accessToken: null,
            cancellationToken: cancellationToken));

        string consentId = SmartAccountsJson.String(payload, "internalConsentId")
            ?? throw new BankDataProviderException(
                "Smart Accounts nu a întors identificatorul consimțământului.");

        return new AuthSession(consentId, ReadTokens(payload));
    }

    private async Task<BankConsentTokens> RefreshAsync(
        string consentId,
        BankConsentTokens current,
        CancellationToken cancellationToken)
    {
        JsonElement payload = SmartAccountsJson.Payload(await SendAsync(
            HttpMethod.Post,
            $"{_options.MtlsBaseUrl.TrimEnd('/')}/gateway/authenticate/rest/api/refreshToken" +
            $"?client_id={Uri.EscapeDataString(_options.ClientId)}" +
            $"&refresh_token={Uri.EscapeDataString(current.RefreshToken)}",
            accessToken: null,
            headers:
            [
                ("access_token", current.AccessToken),
                ("internalConsentId", consentId),
            ],
            cancellationToken: cancellationToken));

        return ReadTokens(payload);
    }

    private static BankConsentTokens ReadTokens(JsonElement payload)
    {
        string? access = SmartAccountsJson.String(payload, "access_token");
        string? refresh = SmartAccountsJson.String(payload, "refresh_token");

        if (access is null || refresh is null)
        {
            throw new BankDataProviderException("Smart Accounts nu a întors tokenurile consimțământului.");
        }

        return new BankConsentTokens(access, refresh, DateTime.UtcNow.Add(AccessTokenLifetime));
    }

    /// <summary>Tokenul de acces valabil al unui consimțământ, reînnoit dacă e pe terminate.</summary>
    private async Task<string> AccessTokenForAsync(string consentId, CancellationToken cancellationToken)
    {
        BankConsentTokens tokens = await tokenStore.GetAsync(consentId, cancellationToken)
            ?? throw new BankDataConsentExpiredException(
                "Conexiunea bancară nu mai are acces valid. Reconectează banca.");

        if (tokens.AccessExpiresAtUtc - RefreshSkew > DateTime.UtcNow)
        {
            return tokens.AccessToken;
        }

        SemaphoreSlim gate = RefreshGates.GetOrAdd(consentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            // Cât am așteptat la poartă, altcineva poate să fi reînnoit deja.
            BankConsentTokens? fresh = await tokenStore.GetAsync(consentId, cancellationToken);

            if (fresh is not null && fresh.AccessExpiresAtUtc - RefreshSkew > DateTime.UtcNow)
            {
                return fresh.AccessToken;
            }

            BankConsentTokens renewed = await RefreshAsync(consentId, fresh ?? tokens, cancellationToken);
            await tokenStore.SaveAsync(consentId, renewed, cancellationToken);

            return renewed.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    // ── HTTP ───────────────────────────────────────────────────────────────────────────────────

    private string CoreUrl(string path) =>
        $"{_options.ApiBaseUrl.TrimEnd('/')}/gateway/core/rest/api/{path}";

    private static string Path(string segment) => Uri.EscapeDataString(segment);

    /// <summary>Apel care are nevoie de tokenul unui consimțământ, cu o reîncercare după reînnoire.</summary>
    private async Task<JsonElement> SendWithConsentAsync(
        HttpMethod method,
        string url,
        string consentId,
        object? body = null,
        IReadOnlyList<(string Name, string Value)>? headers = null,
        bool expectBody = true,
        bool tolerateNotFound = false,
        CancellationToken cancellationToken = default)
    {
        string token = await AccessTokenForAsync(consentId, cancellationToken);

        try
        {
            return await SendAsync(method, url, token, body, headers, expectBody, tolerateNotFound, cancellationToken);
        }
        catch (BankDataUnauthorizedException)
        {
            // Tokenul putea fi invalidat înainte de termen (repornire la ei, rotație pierdută).
            // O singură reîncercare, cu o pereche proaspătă; dacă și aia e refuzată, acordul e mort.
            BankConsentTokens current = await tokenStore.GetAsync(consentId, cancellationToken)
                ?? throw new BankDataConsentExpiredException(
                    "Conexiunea bancară nu mai are acces valid. Reconectează banca.");

            BankConsentTokens renewed;

            try
            {
                renewed = await RefreshAsync(consentId, current, cancellationToken);
            }
            catch (BankDataProviderException ex)
            {
                throw new BankDataConsentExpiredException(
                    $"Acordul dat băncii nu mai e valabil. Reconectează banca. ({ex.Message})");
            }

            await tokenStore.SaveAsync(consentId, renewed, cancellationToken);

            try
            {
                return await SendAsync(
                    method, url, renewed.AccessToken, body, headers, expectBody, tolerateNotFound, cancellationToken);
            }
            catch (BankDataUnauthorizedException)
            {
                throw new BankDataConsentExpiredException(
                    "Acordul dat băncii nu mai e valabil. Reconectează banca.");
            }
        }
    }

    private async Task<JsonElement> SendAsync(
        HttpMethod method,
        string url,
        string? accessToken,
        object? body = null,
        IReadOnlyList<(string Name, string Value)>? headers = null,
        bool expectBody = true,
        bool tolerateNotFound = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new BankDataProviderException("Integrarea Smart Accounts nu este configurată.");
        }

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        foreach ((string name, string value) in headers ?? [])
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response;

        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new BankDataProviderException($"Nu am putut contacta Smart Accounts: {ex.Message}", ex);
        }

        using (response)
        {
            string payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new BankDataRateLimitException(
                    "Smart Accounts a limitat temporar cererile.",
                    response.Headers.RetryAfter?.Delta);
            }

            if (response.StatusCode == HttpStatusCode.NotFound && tolerateNotFound)
            {
                return default;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new BankDataUnauthorizedException(ExtractErrorMessage(payload));
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new BankDataProviderException(
                    $"Cererea către Smart Accounts a eșuat ({(int)response.StatusCode}): {ExtractErrorMessage(payload)}");
            }

            if (!expectBody || string.IsNullOrWhiteSpace(payload))
            {
                return default;
            }

            JsonElement root;

            try
            {
                using var document = JsonDocument.Parse(payload);
                root = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new BankDataProviderException("Smart Accounts a răspuns cu un conținut neașteptat.", ex);
            }

            ThrowIfConsentIsDead(root);

            return root;
        }
    }

    /// <summary>
    /// Un acord expirat sau revocat vine cu 200 și cu starea în corp, nu cu un cod de eroare.
    /// Fără verificarea asta, sincronizarea ar raporta „mers, zero tranzacții" pentru o bancă la
    /// care de fapt nu mai avem acces, iar utilizatorul n-ar afla niciodată că trebuie să
    /// reautorizeze.
    /// </summary>
    private static void ThrowIfConsentIsDead(JsonElement root)
    {
        string? status = SmartAccountsJson.String(SmartAccountsJson.Payload(root), "consentStatus");

        if (status is not null && DeadConsentStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            throw new BankDataConsentExpiredException(
                $"Acordul dat băncii nu mai e valabil ({status}). Reconectează banca.");
        }
    }

    private static string ExtractErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "fără detalii";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            string? message = SmartAccountsJson.String(root, "messageStatus", "message", "error")
                ?? SmartAccountsJson.String(root, "moreDetails");

            if (message is not null)
            {
                return message;
            }
        }
        catch (JsonException)
        {
            // Nu toate erorile vin ca JSON; textul brut e tot o informație.
        }

        return payload.Length > 300 ? payload[..300] : payload;
    }

    private static void Collect(JsonElement container, string property, bool isPending, List<BankTransactionInfo> into)
    {
        if (container.ValueKind != JsonValueKind.Object ||
            !container.TryGetProperty(property, out JsonElement list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in list.EnumerateArray())
        {
            BankTransactionInfo? mapped = MapTransaction(item, isPending);

            if (mapped is not null)
            {
                into.Add(mapped);
            }
        }
    }

    private static BankTransactionInfo? MapTransaction(JsonElement item, bool isPending)
    {
        // Identificatorul stabil e cheia de idempotență a sincronizării. Unele bănci nu trimit
        // `transactionId` la tranzacțiile nefinalizate, dar trimit `entryReference`.
        string? id = SmartAccountsJson.String(item, "transactionId", "entryReference", "endToEndId");

        JsonElement amountNode = item.TryGetProperty("transactionAmount", out JsonElement a) ? a : default;
        decimal? amount = SmartAccountsJson.Decimal(amountNode, "amount");

        if (id is null || amount is null)
        {
            return null;
        }

        // Semnul: la unele bănci suma vine mereu pozitivă, cu direcția în `creditDebitIndicator`.
        string? direction = SmartAccountsJson.String(item, "creditDebitIndicator");

        if (direction is not null && direction.StartsWith("DBIT", StringComparison.OrdinalIgnoreCase) && amount > 0)
        {
            amount = -amount;
        }

        string? counterparty = amount < 0
            ? SmartAccountsJson.String(item, "creditorName", "creditorAccount")
            : SmartAccountsJson.String(item, "debtorName", "debtorAccount");

        return new BankTransactionInfo(
            isPending ? $"pending:{id}" : id,
            SmartAccountsJson.Date(item, "bookingDate", "valueDate", "executionDateTime"),
            SmartAccountsJson.Date(item, "valueDate", "bookingDate"),
            amount.Value,
            SmartAccountsJson.String(amountNode, "currency") ?? "RON",
            counterparty,
            SmartAccountsJson.String(item, "remittanceInformationUnstructured", "additionalInformation"),
            item.GetRawText());
    }
}

/// <summary>
/// 401/403 de la furnizor, înainte de a ști dacă e tokenul sau acordul.
///
/// Nu iese niciodată din provider: e prinsă în <c>SendWithConsentAsync</c>, care încearcă o
/// reînnoire și abia dacă și aia e refuzată o transformă în
/// <see cref="BankDataConsentExpiredException"/> — singura pe care aplicația o interpretează ca
/// „utilizatorul trebuie să reconecteze banca".
/// </summary>
internal sealed class BankDataUnauthorizedException(string message) : BankDataProviderException(message);
