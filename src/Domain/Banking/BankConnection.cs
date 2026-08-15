using SharedKernel;
using Domain.Users;

namespace Domain.Banking;

public sealed class BankConnection : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// Providerul care deține conexiunea (ex. „Fintable"). Rândurile rămase de la un provider
    /// anterior se recunosc după el — nu se convertesc, pentru că nu au echivalent.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    public string InstitutionId { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string? InstitutionLogoUrl { get; set; }

    /// <summary>Provider requisition id, encrypted at rest via ISecretProtector.</summary>
    public string ProviderRequisitionId { get; set; } = string.Empty;

    /// <summary>Provider end-user agreement id, encrypted at rest via ISecretProtector.</summary>
    public string? ProviderAgreementId { get; set; }

    /// <summary>
    /// Referință internă a conexiunii. La providerii cu redirect se întorcea prin `?ref=`;
    /// la unul care nu redirecționează rămâne doar cheia noastră de corelare.
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Când expiră linkul de conectare mintat. După el, o conexiune nerevendicată e pierdută.</summary>
    public DateTime? LinkExpiresAtUtc { get; set; }

    /// <summary>
    /// Conexiunile care existau deja la provider în momentul mintării linkului, ca listă JSON.
    /// Diferența față de lista curentă dă candidații la revendicare — singurul mecanism
    /// disponibil, de vreme ce linkul nu poate purta o referință de-a noastră.
    /// </summary>
    public string? KnownConnectionIdsJson { get; set; }

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
