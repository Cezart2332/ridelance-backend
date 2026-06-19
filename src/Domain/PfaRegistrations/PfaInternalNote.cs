using System;
using Domain.Users;
using SharedKernel;

namespace Domain.PfaRegistrations;

public sealed class PfaInternalNote : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Content { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation
    public PfaRegistration PfaRegistration { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
