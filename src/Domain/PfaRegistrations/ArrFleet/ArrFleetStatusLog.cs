using SharedKernel;

namespace Domain.PfaRegistrations.ArrFleet;

/// <summary>O schimbare de status a procedurii: cine, când, din ce în ce.</summary>
public sealed class ArrFleetStatusLog : Entity
{
    public Guid Id { get; set; }
    public Guid ArrFleetApplicationId { get; set; }
    public ArrFleetStatus FromStatus { get; set; }
    public ArrFleetStatus ToStatus { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }

    public ArrFleetApplication Application { get; set; } = null!;
}
