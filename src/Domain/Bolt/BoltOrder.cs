using SharedKernel;
using Domain.Users;

namespace Domain.Bolt;

public sealed class BoltOrder : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string OrderReference { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string DriverUuid { get; set; } = string.Empty;
    public string? DriverPhone { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime OrderCreatedTime { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string PickupAddress { get; set; } = string.Empty;
    public string DestinationAddress { get; set; } = string.Empty;
    public double RideDistance { get; set; }
    public decimal RidePrice { get; set; }
    public decimal NetEarnings { get; set; }
    public decimal Tip { get; set; }
    public decimal Commission { get; set; }
    public string VehicleModel { get; set; } = string.Empty;
    public string VehicleLicensePlate { get; set; } = string.Empty;
    public DateTime? OrderFinishedTime { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}
