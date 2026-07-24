using SharedKernel;

namespace Domain.PfaRegistrations;

/// <summary>Starea cererii de copie conformă la ARR (Pasul 5). Avans manual, ca la autorizație.</summary>
public enum VehicleCopyRequestStatus
{
    Draft = 0,
    /// <summary>Dosarul PDF copie conformă & ecusoane a fost generat.</summary>
    DossierGenerated = 1,
    /// <summary>Clientul a marcat „Am depus dosarul” la ARR.</summary>
    Submitted = 2,
    /// <summary>Copia conformă a fost emisă și încărcată.</summary>
    Issued = 3,
    Rejected = 4,
}

/// <summary>
/// Pasul 5 — cererea de copie conformă pentru un vehicul, pe o perioadă de ani
/// (vezi <see cref="CopyConformaRules"/>). Taxa/an se snapshot-uiește la generarea dosarului.
/// </summary>
public sealed class VehicleCopyRequest : Entity
{
    public Guid Id { get; set; }
    public Guid PfaVehicleId { get; set; }

    /// <summary>Numărul de ani pentru care se solicită copia conformă.</summary>
    public int Years { get; set; } = 1;

    /// <summary>Taxa copie conformă (bani/an), snapshot la generarea dosarului.</summary>
    public long FeePerYearSnapshotBani { get; set; }
    /// <summary>Total copie conformă (bani) = taxă/an × ani, snapshot la generarea dosarului.</summary>
    public long TotalFeeSnapshotBani { get; set; }

    public VehicleCopyRequestStatus Status { get; set; } = VehicleCopyRequestStatus.Draft;

    // Dosarul generat
    public Guid? DossierDocumentId { get; set; }
    public DateTime? DossierGeneratedAtUtc { get; set; }

    // „Am depus dosarul”
    public DateTime? SubmittedAtUtc { get; set; }

    // Copia conformă emisă
    public Guid? CopyConformaDocumentId { get; set; }
    public string? CopyConformaNumber { get; set; }
    public DateOnly? IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }

    public string? AdminNote { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public PfaVehicle Vehicle { get; set; } = null!;
}
