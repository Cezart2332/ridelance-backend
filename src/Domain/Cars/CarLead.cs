namespace Domain.Cars;

public sealed class CarLead
{
    public Guid Id { get; set; }
    public Guid CarId { get; set; }
    public string CarName { get; set; } = string.Empty; // denormalized for display

    // Applicant info
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string UserPhone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string InterestType { get; set; } = string.Empty; // "Închiriere săptămânală" or "La rămânere"

    // Detaliile cererii (spec §17)
    public CarLeadIntent Intent { get; set; } = CarLeadIntent.Request;

    /// <summary>Data de la care ar vrea mașina. Lipsește dacă n-a completat-o.</summary>
    public DateOnly? PreferredStartDate { get; set; }

    /// <summary>Câte săptămâni: 1, 4 sau 12. Nu are legătură cu prețul — nu avem tarife pe trepte.</summary>
    public int? Weeks { get; set; }

    /// <summary>Are deja cont pe Uber sau Bolt. `null` = n-a răspuns.</summary>
    public bool? HasPlatformAccount { get; set; }

    public string? Message { get; set; }

    /// <summary>Când a bifat acordul de prelucrare a datelor. Fără el, cererea nu se salvează.</summary>
    public DateTime ConsentAcceptedAtUtc { get; set; }

    // Admin
    public CarLeadStatus Status { get; set; } = CarLeadStatus.New;
    public string? AdminNote { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Car Car { get; set; } = null!;
}
