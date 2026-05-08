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
    public string InterestType { get; set; } = string.Empty; // "Închiriere săptămânală" or "La rămânere"

    // Admin
    public CarLeadStatus Status { get; set; } = CarLeadStatus.New;
    public string? AdminNote { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Car Car { get; set; } = null!;
}
