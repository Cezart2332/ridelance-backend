using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Services;
using Domain.Bolt;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0007, IDE0008, IDE0011, S1075, S1144, S3459, S1066

namespace Infrastructure.Services;

/// <summary>
/// Bolt Fleet API 2.0 (developer.bolt.eu/fleet-integration). Toate apelurile sunt POST, răspunsul vine
/// în plicul <c>{code, message, data}</c>, iar erorile vin cu HTTP 200 și <c>code ≠ 0</c> (doar 1005 vine ca 429).
/// </summary>
internal sealed class BoltService(
    IHttpClientFactory httpClientFactory,
    ILogger<BoltService> logger) : IBoltService
{
    private const string BaseUrl = "https://node.bolt.eu/fleet-integration-gateway-v2/fleetIntegration/v2";
    private const int PageSize = 1000;
    private const int MaxRateLimitRetries = 3;

    // Bolt refuză intervale mai lungi de 31 de zile la istoricul de curse (25810 DATE_RANGE_TOO_BIG).
    internal static readonly TimeSpan MaxRideHistoryRange = TimeSpan.FromDays(31);

    private const string FinishedStatus = "finished";
    private const int NotAuthorizedCode = 503;
    private const int TooManyRequestsCode = 1005;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public async Task<string> GetAccessTokenAsync(BoltIntegration integration, CancellationToken cancellationToken)
    {
        // Tokenul Bolt trăiește doar 10 minute: îl refolosim cât mai are cel puțin un minut.
        if (!string.IsNullOrEmpty(integration.AccessToken) &&
            integration.TokenExpiresAtUtc.HasValue &&
            integration.TokenExpiresAtUtc.Value > DateTime.UtcNow.AddMinutes(1))
        {
            return integration.AccessToken;
        }

        logger.LogInformation("Requesting fresh Bolt API access token for client: {ClientId}", integration.ClientId);

        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oidc.bolt.eu/token");

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = integration.ClientId,
            ["client_secret"] = integration.ClientSecret,
            ["grant_type"] = "client_credentials",
            ["scope"] = "fleet-integration:api"
        });
        request.Content = content;

        HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Failed to fetch Bolt token. Status: {Status}, Response: {Response}", response.StatusCode, err);
            throw new Exception($"Eroare autentificare Bolt API: {response.StatusCode}. Detalii: {err}");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<BoltTokenResponse>(cancellationToken: cancellationToken);
        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            throw new Exception("Răspunsul de la Bolt token endpoint este gol sau invalid.");
        }

        integration.AccessToken = tokenResponse.AccessToken;
        integration.TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        integration.IsConnected = true;
        integration.ErrorMessage = null;

        return tokenResponse.AccessToken;
    }

    public async Task<(int CompanyId, string CompanyName)> FetchCompanyIdAsync(string accessToken, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching associated companies from Bolt API...");

        BoltCompaniesData data = await PostAsync<BoltCompaniesData>(accessToken, "getCompanies", null, cancellationToken);

        List<BoltCompanyDto> companies = data.Companies ?? [];
        BoltCompanyDto company = companies.FirstOrDefault(c => string.Equals(c.StatusCode, "active", StringComparison.OrdinalIgnoreCase))
            ?? companies.FirstOrDefault()
            ?? throw new Exception("Nu s-a găsit nicio companie de tip fleet asociată cu acest cont Bolt.");

        string name = string.IsNullOrWhiteSpace(company.LegalName) ? $"Bolt Fleet {company.CompanyId}" : company.LegalName;
        return (company.CompanyId, name);
    }

    public async Task<List<BoltOrder>> FetchOrdersAsync(BoltIntegration integration, DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching orders for company {CompanyId} from {Start} to {End}", integration.CompanyId, start, end);

        var orders = new List<BoltOrder>();
        foreach ((DateTime chunkStart, DateTime chunkEnd) in SplitRange(start, end))
        {
            int offset = 0;
            while (true)
            {
                var body = new
                {
                    company_id = integration.CompanyId,
                    limit = PageSize,
                    offset,
                    order_states = new[] { FinishedStatus },
                    start_datetime = ToIso(chunkStart),
                    end_datetime = ToIso(chunkEnd)
                };

                BoltRideHistoryData page = await PostAsync<BoltRideHistoryData>(integration, "getRideHistory", body, cancellationToken);
                List<BoltRideDto> rides = page.Orders ?? [];

                orders.AddRange(rides
                    .Where(r => string.Equals(r.Status, FinishedStatus, StringComparison.OrdinalIgnoreCase))
                    .Select(r => MapRide(r, integration.UserId)));

                offset += rides.Count;
                if (rides.Count == 0 || offset >= page.Total)
                {
                    break;
                }
            }
        }

        // Paginarea pe offset poate dubla o cursă dacă între pagini apare una nouă.
        return orders
            .GroupBy(o => o.OrderReference)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>Taie intervalul în bucăți de cel mult 31 de zile, cât acceptă getRideHistory.</summary>
    internal static IEnumerable<(DateTime Start, DateTime End)> SplitRange(DateTime start, DateTime end)
    {
        DateTime cursor = start;
        while (cursor < end)
        {
            DateTime next = end - cursor > MaxRideHistoryRange ? cursor + MaxRideHistoryRange : end;
            yield return (cursor, next);
            cursor = next;
        }
    }

    /// <summary>
    /// O cursă V2 în forma salvată la noi. V2 nu mai trimite netul și comisionul pe cursă: venitul Bolt
    /// e brutul (<c>price.total</c>), comisionul vine din factura lunară Bolt.
    /// </summary>
    internal static BoltOrder MapRide(BoltRideDto ride, Guid userId)
    {
        BoltRideDriverDto? driver = ride.Driver;
        string? driverName = !string.IsNullOrWhiteSpace(driver?.DisplayName)
            ? driver.DisplayName
            : JoinNonEmpty(driver?.FirstName, driver?.LastName);

        List<BoltRideStopDto> stops = ride.Stops ?? [];
        string? pickup = stops.FirstOrDefault(s => s.Type == "pickup")?.Address;
        string? dropoff = stops.LastOrDefault(s => s.Type == "dropoff")?.Address;

        return new BoltOrder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderReference = ride.OrderReference ?? string.Empty,
            DriverName = driverName ?? "Șofer Bolt",
            DriverUuid = driver?.Uuid ?? string.Empty,
            DriverPhone = driver?.Phone,
            PaymentMethod = ride.PaymentMethod ?? "card",
            OrderCreatedTime = ride.RequestCreatedDatetime?.UtcDateTime ?? DateTime.UtcNow,
            OrderStatus = FinishedStatus,
            PickupAddress = pickup ?? "Unknown",
            DestinationAddress = dropoff ?? "Unknown",
            // Coloana a rămas în metri, cum o trimitea V1.
            RideDistance = (ride.TotalRideDistanceKm ?? 0) * 1000.0,
            RidePrice = ride.Price?.Total ?? 0m,
            NetEarnings = null,
            Tip = ride.Price?.Tip ?? 0m,
            Commission = null,
            VehicleModel = JoinNonEmpty(ride.Vehicle?.Make, ride.Vehicle?.Model) ?? "Necunoscut",
            VehicleLicensePlate = ride.Vehicle?.RegistrationNumber ?? "Necunoscut",
            OrderFinishedTime = ride.FinishedDatetime?.UtcDateTime
        };
    }

    private async Task<T> PostAsync<T>(BoltIntegration integration, string path, object? body, CancellationToken cancellationToken)
    {
        string token = await GetAccessTokenAsync(integration, cancellationToken);
        try
        {
            return await PostAsync<T>(token, path, body, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Tokenul a expirat între timp: cerem altul o singură dată.
            integration.AccessToken = null;
            integration.TokenExpiresAtUtc = null;
            token = await GetAccessTokenAsync(integration, cancellationToken);
            return await PostAsync<T>(token, path, body, cancellationToken);
        }
    }

    private async Task<T> PostAsync<T>(string accessToken, string path, object? body, CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (true)
        {
            using var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);

            BoltEnvelope<T>? envelope = null;
            try
            {
                envelope = JsonSerializer.Deserialize<BoltEnvelope<T>>(json, JsonOptions);
            }
            catch (JsonException)
            {
                // Corpul nu e plicul Bolt (de ex. o pagină de eroare a gateway-ului): tratat mai jos după status.
            }

            bool rateLimited = response.StatusCode == HttpStatusCode.TooManyRequests || envelope?.Code == TooManyRequestsCode;
            if (rateLimited && attempt < MaxRateLimitRetries)
            {
                TimeSpan delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                logger.LogWarning("Bolt {Path} rate limited, retrying in {Delay}", path, delay);
                await Task.Delay(delay, cancellationToken);
                attempt++;
                continue;
            }

            if (envelope is null || !response.IsSuccessStatusCode && envelope.Code == 0)
            {
                logger.LogError("Bolt {Path} failed. Status: {Status}, Response: {Response}", path, response.StatusCode, json);
                throw new HttpRequestException($"Eroare Bolt API ({path}): {response.StatusCode}", null, response.StatusCode);
            }

            if (envelope.Code != 0 || envelope.Data is null)
            {
                logger.LogError("Bolt {Path} returned {Code} {Message} ({Hint})", path, envelope.Code, envelope.Message, envelope.ErrorHint);
                string hint = string.IsNullOrEmpty(envelope.ErrorHint) ? string.Empty : $" ({envelope.ErrorHint})";
                // 503 NOT_AUTHORIZED: tokenul a expirat sau a fost revocat; apelantul cere altul.
                HttpStatusCode? status = envelope.Code == NotAuthorizedCode ? HttpStatusCode.Unauthorized : null;
                throw new HttpRequestException($"Eroare Bolt API ({path}): {envelope.Message}{hint}", null, status);
            }

            return envelope.Data;
        }
    }

    private static string ToIso(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc)).ToString("yyyy-MM-ddTHH:mm:ssK", System.Globalization.CultureInfo.InvariantCulture);

    private static string? JoinNonEmpty(string? first, string? second)
    {
        string joined = string.Join(' ', new[] { first, second }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return joined.Length == 0 ? null : joined;
    }

    private sealed class BoltTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class BoltEnvelope<T>
    {
        public int Code { get; set; }
        public string? Message { get; set; }

        [JsonPropertyName("error_hint")]
        public string? ErrorHint { get; set; }

        public T? Data { get; set; }
    }

    private sealed class BoltCompaniesData
    {
        public List<BoltCompanyDto>? Companies { get; set; }
    }

    private sealed class BoltCompanyDto
    {
        [JsonPropertyName("company_id")]
        public int CompanyId { get; set; }

        [JsonPropertyName("legal_name")]
        public string? LegalName { get; set; }

        [JsonPropertyName("status_code")]
        public string? StatusCode { get; set; }
    }

    private sealed class BoltRideHistoryData
    {
        public List<BoltRideDto>? Orders { get; set; }
        public int Total { get; set; }
    }

    internal sealed class BoltRideDto
    {
        [JsonPropertyName("order_reference")]
        public string? OrderReference { get; set; }

        public string? Status { get; set; }

        [JsonPropertyName("payment_method")]
        public string? PaymentMethod { get; set; }

        [JsonPropertyName("request_created_datetime")]
        public DateTimeOffset? RequestCreatedDatetime { get; set; }

        [JsonPropertyName("finished_datetime")]
        public DateTimeOffset? FinishedDatetime { get; set; }

        public List<BoltRideStopDto>? Stops { get; set; }

        [JsonPropertyName("total_ride_distance_km")]
        public double? TotalRideDistanceKm { get; set; }

        public BoltRidePriceDto? Price { get; set; }
        public BoltRideDriverDto? Driver { get; set; }
        public BoltRideVehicleDto? Vehicle { get; set; }
    }

    internal sealed class BoltRideStopDto
    {
        public string? Type { get; set; }
        public string? Address { get; set; }
    }

    internal sealed class BoltRidePriceDto
    {
        public decimal? Total { get; set; }
        public decimal? Tip { get; set; }
    }

    internal sealed class BoltRideDriverDto
    {
        public string? Uuid { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        public string? Phone { get; set; }
    }

    internal sealed class BoltRideVehicleDto
    {
        public string? Make { get; set; }
        public string? Model { get; set; }

        [JsonPropertyName("registration_number")]
        public string? RegistrationNumber { get; set; }
    }
}
