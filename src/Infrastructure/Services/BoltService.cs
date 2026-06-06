using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions.Services;
using Domain.Bolt;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0007, IDE0008, IDE0011, S1075, S1144, S3459, S1066

namespace Infrastructure.Services;

internal sealed class BoltService(
    IHttpClientFactory httpClientFactory,
    ILogger<BoltService> logger) : IBoltService
{
    public async Task<string> GetAccessTokenAsync(BoltIntegration integration, CancellationToken cancellationToken)
    {
        // Cache token if valid for at least 5 minutes
        if (!string.IsNullOrEmpty(integration.AccessToken) &&
            integration.TokenExpiresAtUtc.HasValue &&
            integration.TokenExpiresAtUtc.Value > DateTime.UtcNow.AddMinutes(5))
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

        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://node.bolt.eu/fleet-integration-gateway/fleetIntegration/v1/getCompanies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Failed to fetch Bolt companies. Status: {Status}, Response: {Response}", response.StatusCode, err);
            throw new Exception($"Eroare încărcare detalii companie de la Bolt: {response.StatusCode}");
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning("DEBUG: getCompanies JSON: {Json}", json);
        Console.WriteLine($"DEBUG: getCompanies JSON: {json}");
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Try extracting from data.company_ids (returned format: {"code":0,"message":"OK","data":{"company_ids":[178106]}})
        if (root.TryGetProperty("data", out JsonElement dataProp))
        {
            if (dataProp.ValueKind == JsonValueKind.Object && dataProp.TryGetProperty("company_ids", out JsonElement idsProp))
            {
                if (idsProp.ValueKind == JsonValueKind.Array && idsProp.GetArrayLength() > 0)
                {
                    int compId = 0;
                    JsonElement firstId = idsProp[0];
                    if (firstId.ValueKind == JsonValueKind.Number)
                    {
                        compId = firstId.GetInt32();
                    }
                    else if (firstId.ValueKind == JsonValueKind.String && int.TryParse(firstId.GetString(), out int idParsed))
                    {
                        compId = idParsed;
                    }

                    if (compId != 0)
                    {
                        return (compId, $"Bolt Fleet {compId}");
                    }
                }
            }
        }

        // Try extracting from root company_ids
        if (root.TryGetProperty("company_ids", out JsonElement rootIdsProp))
        {
            if (rootIdsProp.ValueKind == JsonValueKind.Array && rootIdsProp.GetArrayLength() > 0)
            {
                int compId = 0;
                JsonElement firstId = rootIdsProp[0];
                if (firstId.ValueKind == JsonValueKind.Number)
                {
                    compId = firstId.GetInt32();
                }
                else if (firstId.ValueKind == JsonValueKind.String && int.TryParse(firstId.GetString(), out int idParsed))
                {
                    compId = idParsed;
                }

                if (compId != 0)
                {
                    return (compId, $"Bolt Fleet {compId}");
                }
            }
        }

        // Original fallback logic for array of objects:
        JsonElement companiesElement = default;
        bool found = false;

        if (root.TryGetProperty("data", out JsonElement dProp))
        {
            if (dProp.TryGetProperty("companies", out JsonElement cos))
            {
                companiesElement = cos;
                found = true;
            }
            else if (dProp.ValueKind == JsonValueKind.Array)
            {
                companiesElement = dProp;
                found = true;
            }
        }

        if (!found && root.TryGetProperty("companies", out JsonElement cosRoot))
        {
            companiesElement = cosRoot;
            found = true;
        }

        if (!found && root.ValueKind == JsonValueKind.Array)
        {
            companiesElement = root;
            found = true;
        }

        if (found && companiesElement.ValueKind == JsonValueKind.Array && companiesElement.GetArrayLength() > 0)
        {
            // If the array contains integers directly, not objects
            if (companiesElement[0].ValueKind == JsonValueKind.Number)
            {
                int directId = companiesElement[0].GetInt32();
                return (directId, $"Bolt Fleet {directId}");
            }
            else if (companiesElement[0].ValueKind == JsonValueKind.String && int.TryParse(companiesElement[0].GetString(), out int parsedId))
            {
                return (parsedId, $"Bolt Fleet {parsedId}");
            }

            JsonElement firstComp = companiesElement[0];
            int compId = 0;
            string compName = "Bolt Fleet Company";

            if (firstComp.TryGetProperty("company_id", out JsonElement cidVal) ||
                firstComp.TryGetProperty("companyId", out cidVal) ||
                firstComp.TryGetProperty("id", out cidVal))
            {
                if (cidVal.ValueKind == JsonValueKind.Number)
                    compId = cidVal.GetInt32();
                else if (cidVal.ValueKind == JsonValueKind.String && int.TryParse(cidVal.GetString(), out int idParsed))
                    compId = idParsed;
            }

            if (firstComp.TryGetProperty("company_name", out JsonElement nameVal) ||
                firstComp.TryGetProperty("companyName", out nameVal) ||
                firstComp.TryGetProperty("name", out nameVal))
            {
                compName = nameVal.GetString() ?? compName;
            }

            if (compId != 0)
            {
                return (compId, compName);
            }
        }

        throw new Exception("Nu s-a găsit nicio companie de tip fleet asociată cu acest cont Bolt.");
    }

    public async Task<List<BoltOrder>> FetchOrdersAsync(BoltIntegration integration, DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        string token = await GetAccessTokenAsync(integration, cancellationToken);

        logger.LogInformation("Fetching orders for company {CompanyId} from {Start} to {End}", integration.CompanyId, start, end);

        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://node.bolt.eu/fleet-integration-gateway/fleetIntegration/v1/getFleetOrders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        long startTs = new DateTimeOffset(start.ToUniversalTime()).ToUnixTimeSeconds();
        long endTs = new DateTimeOffset(end.ToUniversalTime()).ToUnixTimeSeconds();

        var body = new
        {
            offset = 0,
            limit = 1000,
            company_ids = new[] { integration.CompanyId },
            company_id = integration.CompanyId,
            start_ts = startTs,
            end_ts = endTs,
            time_range_filter_type = "price_review"
        };

        request.Content = JsonContent.Create(body);

        HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Failed to fetch Bolt orders. Status: {Status}, Response: {Response}", response.StatusCode, err);
            throw new Exception($"Eroare descărcare comenzi Bolt: {response.StatusCode}");
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        var boltResponse = await response.Content.ReadFromJsonAsync<BoltOrdersResponse>(options, cancellationToken);
        if (boltResponse?.Data?.Orders == null)
        {
            return [];
        }

        var orders = new List<BoltOrder>();
        foreach (var o in boltResponse.Data.Orders)
        {
            // Only fetch and store fully completed orders
            if (o.OrderStatus == null || !o.OrderStatus.Equals("finished", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            orders.Add(new BoltOrder
            {
                Id = Guid.NewGuid(),
                UserId = integration.UserId,
                OrderReference = o.OrderReference ?? string.Empty,
                DriverName = o.DriverName ?? "Șofer Bolt",
                DriverUuid = o.DriverUuid ?? string.Empty,
                DriverPhone = o.DriverPhone,
                PaymentMethod = o.PaymentMethod ?? "Card",
                OrderCreatedTime = DateTimeOffset.FromUnixTimeSeconds(o.OrderCreatedTimestamp ?? 0).UtcDateTime,
                OrderStatus = o.OrderStatus,
                PickupAddress = o.PickupAddress ?? "Unknown",
                DestinationAddress = o.DestinationAddress ?? "Unknown",
                RideDistance = o.RideDistance ?? 0.0,
                RidePrice = o.OrderPrice?.RidePrice ?? 0m,
                NetEarnings = o.OrderPrice?.NetEarnings ?? 0m,
                Tip = o.OrderPrice?.Tip ?? 0m,
                Commission = o.OrderPrice?.Commission ?? 0m,
                VehicleModel = o.VehicleModel ?? "Necunoscut",
                VehicleLicensePlate = o.VehicleLicensePlate ?? "Necunoscut",
                OrderFinishedTime = (o.OrderFinishedTimestamp ?? 0) > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(o.OrderFinishedTimestamp!.Value).UtcDateTime
                    : null
            });
        }

        return orders;
    }

    private sealed class BoltTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class BoltOrdersResponse
    {
        public int Code { get; set; }
        public string? Message { get; set; }
        public BoltOrdersData? Data { get; set; }
    }

    private sealed class BoltOrdersData
    {
        public int CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public int TotalOrders { get; set; }
        public List<BoltOrderDto>? Orders { get; set; }
    }

    private sealed class BoltOrderDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("order_reference")]
        public string? OrderReference { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("driver_name")]
        public string? DriverName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("driver_uuid")]
        public string? DriverUuid { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("driver_phone")]
        public string? DriverPhone { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("payment_method")]
        public string? PaymentMethod { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("order_created_timestamp")]
        public long? OrderCreatedTimestamp { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("order_status")]
        public string? OrderStatus { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pickup_address")]
        public string? PickupAddress { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("destination_address")]
        public string? DestinationAddress { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("ride_distance")]
        public double? RideDistance { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("vehicle_model")]
        public string? VehicleModel { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("vehicle_license_plate")]
        public string? VehicleLicensePlate { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("order_finished_timestamp")]
        public long? OrderFinishedTimestamp { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("order_price")]
        public BoltOrderPriceDto? OrderPrice { get; set; }
    }

    private sealed class BoltOrderPriceDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("ride_price")]
        public decimal? RidePrice { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("net_earnings")]
        public decimal? NetEarnings { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tip")]
        public decimal? Tip { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("commission")]
        public decimal? Commission { get; set; }
    }
}
