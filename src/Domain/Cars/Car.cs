using SharedKernel;

namespace Domain.Cars;

public sealed class Car : Entity
{
    public Guid Id { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }

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

    // Audit
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<CarImage> Images { get; set; } = [];
    public List<CarLead> Leads { get; set; } = [];
}
