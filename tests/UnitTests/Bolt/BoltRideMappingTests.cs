using System.Text.Json;
using Domain.Bolt;
using Infrastructure.Services;
using Shouldly;
using Xunit;

namespace UnitTests.Bolt;

/// <summary>
/// Bolt Fleet API 2.0: o cursă din getRideHistory în forma salvată în bolt_orders. V2 nu mai trimite
/// netul și comisionul pe cursă, deci venitul e brutul (price.total), iar netul și comisionul rămân goale.
/// </summary>
public sealed class BoltRideMappingTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private const string RideJson = """
        {
          "order_reference": "ord-123",
          "type": "regular",
          "status": "finished",
          "is_optional": false,
          "category_name": "Bolt",
          "stops": [
            { "type": "pickup", "address": "Piața Unirii 1, București", "location": { "lat": 44.42, "lng": 26.10 } },
            { "type": "waypoint", "address": "Str. Lipscani 5", "location": { "lat": 44.43, "lng": 26.10 } },
            { "type": "dropoff", "address": "Aeroportul Otopeni", "location": { "lat": 44.57, "lng": 26.08 } }
          ],
          "request_created_datetime": "2026-10-20T10:15:00+03:00",
          "finished_datetime": "2026-10-20T10:52:00+03:00",
          "created_by": "rider",
          "payment_method": "cash",
          "price": { "currency": "ron", "total": 84.5, "tip": 5, "booking_fee": 2, "toll_fee": null, "cancellation_fee": null },
          "total_ride_distance_km": 18.4,
          "driver": { "uuid": "d-1", "partner_uuid": "p-1", "first_name": "Ion", "last_name": "Popescu", "display_name": null, "email": null, "phone": "+40700000000" },
          "vehicle": { "uuid": "v-1", "registration_number": "B 123 ABC", "make": "Toyota", "model": "Corolla", "year": 2022 }
        }
        """;

    private static BoltOrder Map(string json) =>
        BoltService.MapRide(JsonSerializer.Deserialize<BoltService.BoltRideDto>(json, Options)!, Guid.Empty);

    [Fact]
    public void Gross_fare_is_the_ride_price_and_net_and_commission_stay_empty()
    {
        BoltOrder order = Map(RideJson);

        order.RidePrice.ShouldBe(84.5m);
        order.Tip.ShouldBe(5m);
        order.NetEarnings.ShouldBeNull();
        order.Commission.ShouldBeNull();
    }

    [Fact]
    public void Ride_details_come_from_stops_driver_and_vehicle()
    {
        BoltOrder order = Map(RideJson);

        order.OrderReference.ShouldBe("ord-123");
        order.OrderStatus.ShouldBe("finished");
        order.PaymentMethod.ShouldBe("cash");
        order.PickupAddress.ShouldBe("Piața Unirii 1, București");
        order.DestinationAddress.ShouldBe("Aeroportul Otopeni");
        order.DriverName.ShouldBe("Ion Popescu");
        order.DriverUuid.ShouldBe("d-1");
        order.VehicleModel.ShouldBe("Toyota Corolla");
        order.VehicleLicensePlate.ShouldBe("B 123 ABC");
        order.OrderCreatedTime.ShouldBe(new DateTime(2026, 10, 20, 7, 15, 0, DateTimeKind.Utc));
        order.OrderFinishedTime.ShouldBe(new DateTime(2026, 10, 20, 7, 52, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Distance_is_stored_in_meters_like_v1()
    {
        Map(RideJson).RideDistance.ShouldBe(18400, 0.001);
    }

    [Fact]
    public void Display_name_wins_over_first_and_last_name()
    {
        BoltOrder order = Map(RideJson.Replace("\"display_name\": null", "\"display_name\": \"Ion P.\"", StringComparison.Ordinal));

        order.DriverName.ShouldBe("Ion P.");
    }

    [Fact]
    public void Ranges_longer_than_31_days_are_split()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var chunks = BoltService.SplitRange(start, end).ToList();

        chunks.Count.ShouldBe(3);
        chunks[0].Start.ShouldBe(start);
        chunks[^1].End.ShouldBe(end);
        chunks.ShouldAllBe(c => c.End - c.Start <= BoltService.MaxRideHistoryRange);
        chunks.Zip(chunks.Skip(1)).ShouldAllBe(p => p.First.End == p.Second.Start);
    }

    [Fact]
    public void A_short_range_is_a_single_request()
    {
        var end = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);

        BoltService.SplitRange(end.AddDays(-7), end).Count().ShouldBe(1);
    }
}
