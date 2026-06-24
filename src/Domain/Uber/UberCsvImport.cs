using Domain.PfaRegistrations;
using Domain.Users;
using SharedKernel;

namespace Domain.Uber;

public sealed class UberCsvImport : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string FileType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal NetEarnings { get; set; }
    public decimal GrossEarnings { get; set; }
    public decimal CashCollected { get; set; }
    public decimal Commission { get; set; }
    public int Trips { get; set; }
    public double Kilometers { get; set; }
    public double OnlineHours { get; set; }
    public double RideHours { get; set; }

    public User User { get; set; } = null!;
    public PfaRegistration PfaRegistration { get; set; } = null!;
}
