using SharedKernel;

namespace Domain.PfaRegistrations;

/// <summary>Starea setului de ecusoane pentru o platformă (Pasul 5). Avans manual.</summary>
public enum VehicleBadgeStatus
{
    /// <summary>Clientul a solicitat ecusoanele.</summary>
    Requested = 0,
    /// <summary>Taxa a fost plătită.</summary>
    Paid = 1,
    /// <summary>Ecusoanele au fost emise și încărcate.</summary>
    Issued = 2,
}

/// <summary>
/// Pasul 5 — ecusoane per platformă (Uber/Bolt), 8 lei/set (taxa din <c>AppSetting</c>).
/// Fiecare vehicul poate avea un rând per platformă selectată.
/// </summary>
public sealed class VehicleBadge : Entity
{
    public Guid Id { get; set; }
    public Guid PfaVehicleId { get; set; }

    public PfaPlatformProvider Provider { get; set; }

    /// <summary>Numărul de seturi de ecusoane solicitate.</summary>
    public int SetCount { get; set; } = 1;

    /// <summary>Taxa per set (bani), snapshot la solicitare.</summary>
    public long FeePerSetSnapshotBani { get; set; }
    /// <summary>Total ecusoane (bani) = taxă/set × seturi, snapshot la solicitare.</summary>
    public long TotalFeeSnapshotBani { get; set; }

    public VehicleBadgeStatus Status { get; set; } = VehicleBadgeStatus.Requested;

    /// <summary>Ecusonul emis (categorie EcusonUber/EcusonBolt).</summary>
    public Guid? BadgeDocumentId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public PfaVehicle Vehicle { get; set; } = null!;
}
