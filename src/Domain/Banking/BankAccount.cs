using SharedKernel;

namespace Domain.Banking;

public sealed class BankAccount : Entity
{
    public Guid Id { get; set; }
    public Guid BankConnectionId { get; set; }

    /// <summary>Denormalized owner for cheap per-user queries (same trick as BoltOrder).</summary>
    public Guid UserId { get; set; }

    public string ProviderAccountId { get; set; } = string.Empty;

    /// <summary>Masked IBAN only (e.g. "RO49••••1234") — the full IBAN is never stored.</summary>
    public string? IbanMasked { get; set; }

    public string? Currency { get; set; }
    public string? OwnerName { get; set; }

    public DateTime? LastTransactionsSyncedAtUtc { get; set; }

    /// <summary>Persisted backoff after a provider/bank 429 — no sync until this passes.</summary>
    public DateTime? RateLimitedUntilUtc { get; set; }

    public bool IsActive { get; set; }

    // Navigation
    public BankConnection Connection { get; set; } = null!;
}
