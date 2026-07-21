using Domain.Users;
using SharedKernel;

namespace Domain.PfaRegistrations;

public sealed class OnboardingSectionApproval : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public OnboardingSectionKey SectionKey { get; set; }
    public OnboardingSectionStatus Status { get; set; } = OnboardingSectionStatus.Locked;
    public string? Note { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ValidatedAtUtc { get; set; }
    public Guid? ValidatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public PfaRegistration PfaRegistration { get; set; } = null!;
    public User? ValidatedByUser { get; set; }
}
