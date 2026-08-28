using SharedKernel;

namespace Domain.Rentals;

/// <summary>Predare sau primire. Aceleași câmpuri de stare, plus deconturile la returnare.</summary>
public enum CheckKind
{
    CheckIn,
    CheckOut,
}

/// <summary>Unghiurile fotografiate, aceleași la predare și la primire, ca să se poată compara.</summary>
public enum CheckPhotoSlot
{
    Front,
    Rear,
    Left,
    Right,
    Interior,
    Dashboard,
    Extra,
}

/// <summary>
/// Starea mașinii la predare sau la primire, consemnată.
/// </summary>
/// <remarks>
/// O singură entitate pentru ambele momente, discriminată prin <see cref="Kind"/>: câmpurile sunt
/// aceleași — kilometraj, combustibil, accesorii, fotografii — iar două tabele identice ar fi
/// însemnat două locuri în care se scrie același lucru.
///
/// Deconturile apar doar la primire. Sunt nullable, nu zero: „n-am reținut nimic" și „încă nu s-a
/// completat" sunt lucruri diferite pe un proces-verbal.
/// </remarks>
public sealed class CheckRecord : Entity
{
    public Guid Id { get; set; }

    public Guid RentalId { get; set; }
    public Rental Rental { get; set; } = null!;

    public CheckKind Kind { get; set; }

    /// <summary>Momentul real al predării, nu cel planificat în contract.</summary>
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public int Mileage { get; set; }

    /// <summary>Cum se citește de pe bord: „plin", „3/4", „80%".</summary>
    public string? FuelLevel { get; set; }

    /// <summary>Ce s-a predat efectiv, ca listă. Se compară cu ce s-a primit înapoi.</summary>
    public List<string> Accessories { get; set; } = [];

    public string? Notes { get; set; }

    // --- Doar la primire ---

    public long? DepositReturnedBani { get; set; }
    public long? DepositWithheldBani { get; set; }

    /// <summary>De ce s-a reținut. Obligatoriu când se reține ceva: o sumă fără motiv e o dispută.</summary>
    public string? WithholdingReason { get; set; }

    public long? ExtraMileageChargeBani { get; set; }
    public long? OtherChargesBani { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<CheckPhoto> Photos { get; set; } = [];
}

/// <summary>O fotografie pe un slot. Fișierul trece prin `Document`, criptat ca oricare altul.</summary>
public sealed class CheckPhoto : Entity
{
    public Guid Id { get; set; }

    public Guid CheckRecordId { get; set; }
    public CheckRecord CheckRecord { get; set; } = null!;

    public CheckPhotoSlot Slot { get; set; }
    public Guid DocumentId { get; set; }

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
