using System;
using Domain.Users;
using SharedKernel;

namespace Domain.PfaRegistrations;

public sealed class PfaActivityLog : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public string ActivityType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid PerformedByUserId { get; set; }

    // Navigation
    public PfaRegistration PfaRegistration { get; set; } = null!;
    public User PerformedByUser { get; set; } = null!;
}
