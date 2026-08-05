using SharedKernel;

namespace Domain.Documents;

/// <summary>
/// Un câmp extras din document prin OCR (Gemini prin OpenRouter), cu proveniență și audit.
/// Specificația cere: valoarea OCR, valoarea confirmată, cine a modificat, când și de ce,
/// plus documentul-sursă. <see cref="AiValue"/> este imutabil — nu se suprascrie niciodată.
/// Sursa de adevăr rămâne coloana de pe entitatea de business (ex. <c>PfaRegistration.Cui</c>);
/// acest tabel e stratul de proveniență.
/// </summary>
public sealed class ExtractedField : Entity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }

    /// <summary>Cheia câmpului din catalogul AI (ex. "cui", "vin", "iban", "expiry").</summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>Valoarea brută raportată de model. Imutabilă — rămâne ca dovadă.</summary>
    public string? AiValue { get; set; }

    /// <summary>Valoarea normalizată (trim, uppercase plăcuțe, ISO date etc.).</summary>
    public string? AiNormalizedValue { get; set; }

    /// <summary>Încrederea auto-raportată de model (0..1).</summary>
    public double AiConfidence { get; set; }

    /// <summary>A trecut validatorul determinist din Domain (CUI, IBAN, VIN, plăcuță, dată).</summary>
    public bool ValidatorPassed { get; set; }

    /// <summary>
    /// Încrederea efectivă folosită la decizii:
    /// <c>ValidatorPassed ? AiConfidence : min(AiConfidence, 0.30)</c>.
    /// </summary>
    public double EffectiveConfidence { get; set; }

    /// <summary>Valoarea confirmată de om; câștigă întotdeauna față de <see cref="AiValue"/>.</summary>
    public string? ConfirmedValue { get; set; }

    public ExtractedFieldSource ConfirmedSource { get; set; } = ExtractedFieldSource.None;
    public Guid? ConfirmedByUserId { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }

    /// <summary>Motivul modificării — obligatoriu când adminul corectează o valoare.</summary>
    public string? ChangeReason { get; set; }

    /// <summary>Câmp sensibil (nu se loghează / nu se întoarce în clar în răspunsuri AI).</summary>
    public bool IsSensitive { get; set; }

    /// <summary>
    /// Valoarea în clar a unui câmp sensibil, criptată cu ISecretProtector. Pentru câmpurile
    /// sensibile, <see cref="AiValue"/>, <see cref="AiNormalizedValue"/> și
    /// <see cref="ConfirmedValue"/> conțin DOAR masca de afișare — valoarea reală trăiește
    /// exclusiv aici, ca un CNP să nu ajungă niciodată în clar în baza de date sau în loguri.
    /// </summary>
    public string? EncryptedValue { get; set; }

    public ExtractedFieldReviewState ReviewState { get; set; } = ExtractedFieldReviewState.Auto;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Document Document { get; set; } = null!;
}
