using SharedKernel;

namespace Domain.PfaRegistrations;

/// <summary>
/// Pasul 2.3 — declarația contului bancar al PFA-ului. IBAN-ul complet e criptat la rest
/// (ISecretProtector) și se stochează mascat pentru afișare. Dacă userul are deja o conexiune
/// PSD2 activă (<c>BankConnection</c>), datele se precompletează și se validează automat.
/// </summary>
public sealed class PfaBankAccountDeclaration : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    public string? BankName { get; set; }

    /// <summary>IBAN complet, criptat cu ISecretProtector — niciodată în clar în DB.</summary>
    public string? IbanEncrypted { get; set; }

    /// <summary>IBAN mascat pentru afișare (ex. "RO49••••1234").</summary>
    public string? IbanMasked { get; set; }

    /// <summary>Documentul de confirmare a contului (extras/scrisoare bancă).</summary>
    public Guid? ConfirmationDocumentId { get; set; }

    /// <summary>OCR-ul de pe documentul de confirmare a găsit același IBAN (null = neverificat).</summary>
    public bool? OcrIbanMatches { get; set; }

    /// <summary>Conexiunea PSD2 din care s-a precompletat, dacă e cazul.</summary>
    public Guid? BankConnectionId { get; set; }

    public BankDeclarationSource Source { get; set; } = BankDeclarationSource.Manual;
    public BankDeclarationStatus Status { get; set; } = BankDeclarationStatus.Pending;
    public string? AdminNote { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public PfaRegistration PfaRegistration { get; set; } = null!;
}
