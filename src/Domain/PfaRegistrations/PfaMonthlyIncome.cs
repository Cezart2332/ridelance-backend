using Domain.Users;

namespace Domain.PfaRegistrations;

public sealed class PfaMonthlyIncome
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal VenitCash { get; set; }
    public decimal VenitCard { get; set; }
    public decimal VenitBolt { get; set; }
    public decimal VenitUber { get; set; }
    public decimal TaxeEstimate { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid UpdatedByUserId { get; set; }
    public bool IsProcessed { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public Guid? ProcessedByUserId { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
    public User? ProcessedByUser { get; set; }

    public decimal ComputeVenitTotal() => VenitCash + VenitCard + VenitBolt + VenitUber;
}
