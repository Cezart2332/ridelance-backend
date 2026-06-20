using SharedKernel;

namespace Domain.PfaRegistrations;

public sealed class PfaPlatformAccount : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public PfaPlatformProvider Provider { get; set; }
    public PfaPlatformAccountKind Kind { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? FullName { get; set; }
    public string? PasswordProtected { get; set; }
    public DateTime? PasswordUpdatedAtUtc { get; set; }
    public PfaFleetAccountStatus Status { get; set; } = PfaFleetAccountStatus.NotConfigured;
    public DateTime? ConfiguredAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
}
