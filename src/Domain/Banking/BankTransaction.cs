using SharedKernel;

namespace Domain.Banking;

public sealed class BankTransaction : Entity
{
    public Guid Id { get; set; }
    public Guid BankAccountId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Acordul Smart Accounts care a furnizat tranzacția; null pentru datele vechi neverificabile.</summary>
    public string? ProviderConsentId { get; set; }

    /// <summary>Identificatorul furnizorului, unic în cadrul contului și acordului.</summary>
    public string ProviderTransactionId { get; set; } = string.Empty;

    public DateOnly? BookingDate { get; set; }
    public DateOnly? ValueDate { get; set; }

    /// <summary>Signed amount: positive = credit (încasare), negative = debit (cheltuială).</summary>
    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;
    public string? CounterpartyName { get; set; }
    public string? RemittanceInfo { get; set; }
    public bool IsPending { get; set; }

    /// <summary>Raw provider payload (jsonb) — keeps fields phase-2 classification may need.</summary>
    public string? RawJson { get; set; }

    // Phase 2 (auto-classification) — populated later, no schema rework needed.
    public string? Category { get; set; }
    public string? MatchedSource { get; set; }
    public Guid? MatchedDocumentId { get; set; }
    public DateTime? ClassifiedAtUtc { get; set; }

    public DateTime ImportedAtUtc { get; set; }

    // Navigation
    public BankAccount Account { get; set; } = null!;
}
