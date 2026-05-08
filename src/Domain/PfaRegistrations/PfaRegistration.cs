using Domain.Documents;
using Domain.Users;
using SharedKernel;

namespace Domain.PfaRegistrations;

public sealed class PfaRegistration : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public RegistrationType RegistrationType { get; set; }
    public PfaRegistrationStatus Status { get; set; } = PfaRegistrationStatus.Pending;

    // "Am PFA" fields
    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public string? Cui { get; set; }

    // "Nu am PFA" fields
    public int? ContractDuration { get; set; }
    public string? Street { get; set; }
    public string? Number { get; set; }
    public string? City { get; set; }
    public string? County { get; set; }
    public bool IsOwner { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public Guid? AssignedContabilId { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public User? ReviewedByUser { get; set; }
    public User? AssignedContabil { get; set; }
    public List<Document> Documents { get; set; } = [];
}
