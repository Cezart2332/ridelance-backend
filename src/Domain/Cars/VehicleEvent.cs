using SharedKernel;

namespace Domain.Cars;

/// <summary>Ce s-a întâmplat. Enum, ca interfața să poată alege pictograma fără să citească textul.</summary>
public enum VehicleEventType
{
    RentalOpened,
    RentalClosed,
    DocumentGenerated,
    DocumentSigned,
    CheckIn,
    CheckOut,
    DocumentUploaded,
    Maintenance,
}

/// <summary>
/// Cronologia unei mașini. Se scrie singură, din acțiunile sistemului.
/// </summary>
/// <remarks>
/// Append-only și niciodată completată de utilizator (spec §10). Un istoric pe care îl poate scrie
/// cineva de mână nu mai e istoric: e o listă de afirmații. Rândurile apar din handlerele care
/// chiar fac lucrurile — s-a generat un document, s-a semnat, s-a predat mașina — deci nu pot
/// descrie ceva ce nu s-a întâmplat.
///
/// Textul se compune la scriere, nu la citire: „RCA nou încărcat" trebuie să rămână ce s-a
/// întâmplat atunci, chiar dacă documentul se șterge mâine.
/// </remarks>
public sealed class VehicleEvent : Entity
{
    public Guid Id { get; set; }

    public Guid CarId { get; set; }

    public VehicleEventType Type { get; set; }

    /// <summary>Ce se citește în cronologie. Compus la scriere.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Închirierea din care a venit, când e cazul. Fără FK: evenimentul supraviețuiește.</summary>
    public Guid? RentalId { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
