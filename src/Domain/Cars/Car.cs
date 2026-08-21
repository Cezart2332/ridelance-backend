using SharedKernel;

namespace Domain.Cars;

public sealed class Car : Entity
{
    public Guid Id { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }

    /// <summary>Identitatea din URL-ul public (<see cref="CarSlug"/>). Unică pe tot tabelul.</summary>
    public string Slug { get; set; } = string.Empty;

    // Specs
    public string Engine { get; set; } = string.Empty;       // Electric, Hybrid, GPL, Benzină, Diesel
    public string Transmission { get; set; } = string.Empty; // Automată, Manuală

    // Location – stored as comma-separated or JSON string
    public string Location { get; set; } = string.Empty;

    // Pricing
    public decimal PricePerWeek { get; set; }
    public decimal? OldPrice { get; set; }
    public bool DiscountActive { get; set; }
    public decimal? Garantie { get; set; }

    // Classification
    public CarOfferType OfferType { get; set; } = CarOfferType.Weekly;
    public CarStatus Status { get; set; } = CarStatus.Available;

    // Platform categories (stored as JSON arrays)
    public List<string> UberCategories { get; set; } = [];
    public List<string> BoltCategories { get; set; } = [];
    public List<string> Badges { get; set; } = [];

    // Content
    public string Description { get; set; } = string.Empty;

    // Visibility
    public bool Active { get; set; } = true;

    // Listing metadata
    public Guid? PostedByUserId { get; set; }
    public CarListingSource ListingSource { get; set; } = CarListingSource.Ridelance;
    public CarApprovalStatus ApprovalStatus { get; set; } = CarApprovalStatus.Approved;
    public CarListingPaymentStatus PaymentStatus { get; set; } = CarListingPaymentStatus.NotRequired;
    public string? StripeCheckoutSessionId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    // Audit
    /// <summary>
    /// Scorul „Recomandate", 0–100 (spec §5.2).
    /// </summary>
    /// <remarks>
    /// Stocat, nu calculat la fiecare cerere: sortarea marketplace-ului nu are voie să depindă de
    /// cât durează un calcul per rând. Se recalculează la evenimentele care îl pot schimba și,
    /// pentru componenta de prospețime, printr-un job nocturn.
    /// </remarks>
    public int RecommendationScore { get; set; }

    /// <summary>Când a fost calculat ultima dată. `null` = niciodată, deci scorul e 0 implicit.</summary>
    public DateTime? ScoreComputedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Analytics (forms = Leads.Count)
    public int ViewCount { get; set; }

    /// <summary>Vizitatori distincți, după hash. Un refresh nu îl mai mișcă.</summary>
    public int UniqueViewCount { get; set; }
    public int ClickCount { get; set; }

    // Navigation
    public List<CarImage> Images { get; set; } = [];
    public List<CarLead> Leads { get; set; } = [];
}
