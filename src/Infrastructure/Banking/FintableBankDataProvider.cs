using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions.Services;
using Microsoft.Extensions.Options;

namespace Infrastructure.Banking;

/// <summary>
/// Providerul de open banking Fintable (https://fintable.io/api/v2).
///
/// Diferența față de providerii PSD2 clasici: nu există autorizare per utilizator și nici
/// redirect înapoi la noi. Se mintează un link, utilizatorul îl parcurge la Fintable, iar
/// conexiunea apare pur și simplu în contul nostru. Cine e proprietarul ei o decide
/// <c>BankConnectionClaimService</c>, nu providerul — de aceea aici nu există noțiunea de user.
///
/// Autentificarea e un simplu bearer token de cont, deci fără JWT, fără cache de token și
/// fără reînnoire.
/// </summary>
internal sealed class FintableBankDataProvider(
    HttpClient httpClient,
    IOptions<FintableOptions> optionsAccessor)
    : IBankDataProvider
{
    private readonly FintableOptions _options = optionsAccessor.Value;

    /// <summary>Maximul acceptat de API pe o pagină de tranzacții.</summary>
    private const int TransactionPageSize = 500;

    public string ProviderName => "Fintable";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Token);

    public async Task<IReadOnlyList<BankInstitutionInfo>> ListInstitutionsAsync(
        string countryCode,
        CancellationToken cancellationToken = default)
    {
        JsonElement root = await SendAsync(
            HttpMethod.Get,
            BuildPath("institutions", ("country", countryCode)),
            cancellationToken: cancellationToken);

        JsonElement data = FintableJson.Data(root);
        if (data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<BankInstitutionInfo> institutions = [];
        foreach (JsonElement item in data.EnumerateArray())
        {
            // Slug-ul e ce cere `POST /connections/link`; id-ul numeric nu ne folosește.
            string? id = FintableJson.String(item, "slug", "id");
            string? name = FintableJson.String(item, "name", "display_name");
            if (id is null || name is null)
            {
                continue;
            }

            institutions.Add(new BankInstitutionInfo(
                id,
                name,
                FintableJson.String(item, "logo", "logo_url", "icon")));
        }

        return institutions
            .OrderBy(i => i.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task<BankLinkCreated> MintConnectionLinkAsync(
        string? institutionId,
        CancellationToken cancellationToken = default)
    {
        // Singurul parametru acceptat de API. Nu putem atașa nicio referință proprie —
        // de aici întreaga nevoie de revendicare pe partea noastră.
        var body = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(institutionId))
        {
            body["institution"] = institutionId;
        }

        JsonElement root = await SendAsync(
            HttpMethod.Post,
            BuildPath("connections/link"),
            body,
            cancellationToken: cancellationToken);

        return ReadLink(root);
    }

    public async Task<BankLinkCreated> MintReconnectLinkAsync(
        string providerConnectionId,
        CancellationToken cancellationToken = default)
    {
        JsonElement root = await SendAsync(
            HttpMethod.Post,
            BuildPath($"connections/{Uri.EscapeDataString(providerConnectionId)}/link"),
            new Dictionary<string, string?>(),
            cancellationToken: cancellationToken);

        return ReadLink(root);
    }

    public async Task<IReadOnlyList<BankProviderConnection>> ListConnectionsAsync(
        CancellationToken cancellationToken = default)
    {
        JsonElement root = await SendAsync(
            HttpMethod.Get,
            BuildPath("connections"),
            cancellationToken: cancellationToken);

        JsonElement data = FintableJson.Data(root);
        if (data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<BankProviderConnection> connections = [];
        foreach (JsonElement item in data.EnumerateArray())
        {
            string? id = FintableJson.String(item, "id");
            if (id is null)
            {
                continue;
            }

            connections.Add(new BankProviderConnection(
                id,
                FintableJson.String(item, "institution_slug", "institution_id"),
                FintableJson.String(item, "institution", "institution_name", "name"),
                FintableJson.String(item, "logo", "logo_url"),
                FintableJson.String(item, "status", "state"),
                FintableJson.Timestamp(item, "created_at", "created_at_utc")));
        }

        return connections;
    }

    public async Task<IReadOnlyList<string>> ListAccountsAsync(
        string providerConnectionId,
        CancellationToken cancellationToken = default)
    {
        JsonElement root = await SendAsync(
            HttpMethod.Get,
            BuildPath("accounts", ("connection_id", providerConnectionId)),
            cancellationToken: cancellationToken);

        JsonElement data = FintableJson.Data(root);
        if (data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. data.EnumerateArray()
            .Select(item => FintableJson.String(item, "id"))
            .Where(id => id is not null)
            .Select(id => id!)];
    }

    public async Task<BankAccountDetailsInfo> GetAccountDetailsAsync(
        string providerAccountId,
        CancellationToken cancellationToken = default)
    {
        JsonElement root = await SendAsync(
            HttpMethod.Get,
            BuildPath($"accounts/{Uri.EscapeDataString(providerAccountId)}"),
            cancellationToken: cancellationToken);

        JsonElement account = FintableJson.Data(root);

        // IBAN-ul nu e documentat pe cont. Îl citim dacă apare; când lipsește rămâne null, iar
        // verificarea automată a declarației bancare din onboarding cade pe fluxul manual.
        return new BankAccountDetailsInfo(
            FintableJson.String(account, "iban", "account_number", "number"),
            FintableJson.String(account, "currency", "currency_code"),
            FintableJson.String(account, "owner_name", "holder_name", "name"));
    }

    public async Task<BankTransactionsPage> GetTransactionsAsync(
        string providerAccountId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken = default)
    {
        // `pending` e filtru, nu câmp garantat în răspuns, deci se cer separat cele două seturi.
        IReadOnlyList<BankTransactionInfo> booked =
            await FetchAsync(providerAccountId, dateFrom, dateTo, pending: false, cancellationToken);
        IReadOnlyList<BankTransactionInfo> pending =
            await FetchAsync(providerAccountId, dateFrom, dateTo, pending: true, cancellationToken);

        return new BankTransactionsPage(booked, pending);
    }

    public async Task TriggerSyncAsync(
        string providerConnectionId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(
            HttpMethod.Post,
            BuildPath($"sync/{Uri.EscapeDataString(providerConnectionId)}"),
            new Dictionary<string, string?>(),
            expectBody: false,
            cancellationToken: cancellationToken);
    }

    public async Task DeleteConnectionAsync(
        string providerConnectionId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(
            HttpMethod.Delete,
            BuildPath($"connections/{Uri.EscapeDataString(providerConnectionId)}"),
            expectBody: false,
            tolerateNotFound: true,
            cancellationToken: cancellationToken);
    }

    /// <summary>Parcurge paginile prin cursor până la epuizare, cu plafon de siguranță.</summary>
    private async Task<IReadOnlyList<BankTransactionInfo>> FetchAsync(
        string providerAccountId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        bool pending,
        CancellationToken cancellationToken)
    {
        List<BankTransactionInfo> transactions = [];
        string? cursor = null;

        for (int page = 0; page < _options.MaxTransactionPages; page++)
        {
            var query = new List<(string, string?)>
            {
                ("limit", TransactionPageSize.ToString(CultureInfo.InvariantCulture)),
                ("pending", pending ? "true" : "false"),
                ("include", "raw"),
                ("date_from", dateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("date_to", dateTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("cursor", cursor),
            };

            JsonElement root = await SendAsync(
                HttpMethod.Get,
                BuildPath($"accounts/{Uri.EscapeDataString(providerAccountId)}/transactions", [.. query]),
                cancellationToken: cancellationToken);

            JsonElement data = FintableJson.Data(root);
            if (data.ValueKind == JsonValueKind.Array)
            {
                transactions.AddRange(data.EnumerateArray().Select(MapTransaction).Where(t => t is not null)!);
            }

            cursor = FintableJson.Cursor(root);
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return transactions;
    }

    private static BankTransactionInfo? MapTransaction(JsonElement item)
    {
        string? id = FintableJson.String(item, "id");
        decimal? amount = FintableJson.Decimal(item, "amount");

        // Fără id sau fără sumă nu avem ce înregistra; o linie pe jumătate citită ar strica
        // totalurile mai tăcut decât dacă o sărim.
        if (id is null || amount is null)
        {
            return null;
        }

        return new BankTransactionInfo(
            id,
            FintableJson.Date(item, "date", "booked_date", "booking_date"),
            FintableJson.Date(item, "value_date", "posted_date", "date"),
            amount.Value,
            FintableJson.String(item, "currency", "currency_code") ?? "RON",
            FintableJson.String(item, "counterparty", "merchant", "counterparty_name"),
            FintableJson.String(item, "description", "remittance_info", "memo"),
            item.GetRawText());
    }

    private static BankLinkCreated ReadLink(JsonElement root)
    {
        JsonElement data = FintableJson.Data(root);
        string address = FintableJson.String(data, "url", "link")
            ?? throw new BankDataProviderException("Răspunsul Fintable la crearea linkului nu conține un URL.");

        return new BankLinkCreated(address, FintableJson.Timestamp(data, "expires_at", "expires_at_utc"));
    }

    private string BuildPath(string path, params (string Key, string? Value)[] query)
    {
        List<string> parts = [];

        foreach ((string key, string? value) in query)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
            }
        }

        // Workspace-ul se selectează întotdeauna prin query, chiar și pe POST/DELETE —
        // API-ul nu îl citește niciodată din body.
        if (!string.IsNullOrWhiteSpace(_options.WorkspaceId))
        {
            parts.Add($"workspace_id={Uri.EscapeDataString(_options.WorkspaceId)}");
        }

        string baseUrl = _options.BaseUrl.TrimEnd('/');
        string query_ = parts.Count > 0 ? $"?{string.Join('&', parts)}" : string.Empty;
        return $"{baseUrl}/{path}{query_}";
    }

    private async Task<JsonElement> SendAsync(
        HttpMethod method,
        string url,
        object? body = null,
        bool expectBody = true,
        bool tolerateNotFound = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new BankDataProviderException("Integrarea Fintable nu este configurată.");
        }

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

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
            throw new BankDataProviderException("Nu s-a putut contacta API-ul Fintable.", ex);
        }

        using (response)
        {
            string payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new BankDataRateLimitException(
                    "Limita de apeluri către bancă a fost atinsă. Sincronizarea va fi reluată automat.",
                    response.Headers.RetryAfter?.Delta);
            }

            if (tolerateNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            bool consentExpired =
                response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized &&
                (payload.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                 payload.Contains("revoked", StringComparison.OrdinalIgnoreCase) ||
                 payload.Contains("reconnect", StringComparison.OrdinalIgnoreCase));

            if (consentExpired)
            {
                throw new BankDataConsentExpiredException(
                    "Conexiunea cu banca nu mai este validă. Reconectează contul bancar.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new BankDataProviderException(
                    $"Cererea către Fintable a eșuat ({(int)response.StatusCode}): {ExtractErrorMessage(payload)}");
            }

            if (!expectBody || string.IsNullOrWhiteSpace(payload))
            {
                return default;
            }

            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
    }

    private static string ExtractErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "răspuns gol";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out JsonElement error))
            {
                return FintableJson.String(error, "message", "type") ?? payload;
            }
        }
        catch (JsonException)
        {
            // Nu tot ce eșuează întoarce JSON; textul brut e mai util decât o excepție nouă.
        }

        return payload.Length > 300 ? payload[..300] : payload;
    }
}
