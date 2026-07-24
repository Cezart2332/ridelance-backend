namespace Domain.Documents;

/// <summary>
/// Cine a confirmat valoarea finală a unui câmp extras prin OCR.
/// Valoarea AI rămâne mereu ca dovadă; confirmarea umană câștigă.
/// </summary>
public enum ExtractedFieldSource
{
    None = 0,
    User = 1,
    Admin = 2,
}
