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

    /// <summary>
    /// Total income for the month. Cash and card are the payment-method split of the platform
    /// money, not extra income, so the two views describe the same amount and are never added
    /// together. Whichever view is filled in wins — the split is derived automatically when Bolt
    /// or Uber data exists, and typed in by hand for months without it.
    /// </summary>
    public decimal ComputeVenitTotal() => Math.Max(VenitBolt + VenitUber, VenitCash + VenitCard);

    public decimal ComputePlatformIncome() => VenitBolt + VenitUber;
}
