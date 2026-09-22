namespace Domain.FiscalProfiles;

/// <summary>
/// O modificare a profilului fiscal: confirmarea PFA-ului sau o editare ulterioară. Ciornele
/// salvate automat nu lasă revizii — nu schimbă nimic din ce vede cineva.
/// </summary>
public sealed class PfaTaxProfileRevision
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public int Revision { get; set; }
    public Guid ActorUserId { get; set; }

    /// <summary><c>pfa</c>, <c>admin</c> sau <c>accounting</c>.</summary>
    public string ActorRole { get; set; } = string.Empty;

    /// <summary>Câmpurile schimbate, cu valoarea veche și cea nouă (JSON).</summary>
    public string ChangesJson { get; set; } = "[]";

    /// <summary>Obligatoriu pentru staff; <c>null</c> doar pentru PFA.</summary>
    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public PfaTaxProfile Profile { get; set; } = null!;
}
