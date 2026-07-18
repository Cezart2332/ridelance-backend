using SharedKernel;
using Domain.Users;

namespace Domain.Banking;

public sealed class BankConnection : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Data provider identifier (e.g. "GoCardless") — allows swapping providers later.</summary>
    public string Provider { get; set; } = string.Empty;

    public string InstitutionId { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string? InstitutionLogoUrl { get; set; }

    /// <summary>Provider requisition id, encrypted at rest via ISecretProtector.</summary>
    public string ProviderRequisitionId { get; set; } = string.Empty;

    /// <summary>Provider end-user agreement id, encrypted at rest via ISecretProtector.</summary>
    public string? ProviderAgreementId { get; set; }

    /// <summary>Our own reference passed to the provider; comes back in the redirect (?ref=).</summary>
    public string Reference { get; set; } = string.Empty;

    public BankConnectionStatus Status { get; set; }
    public DateTime? ConsentExpiresAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? ExpiryNotifiedAtUtc { get; set; }

    public int MaxHistoricalDays { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LinkedAtUtc { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public List<BankAccount> Accounts { get; set; } = [];
}
