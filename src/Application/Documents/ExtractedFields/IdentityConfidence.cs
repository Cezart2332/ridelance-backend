namespace Application.Documents.ExtractedFields;

/// <summary>
/// Cât de sigur trebuie să fie OCR-ul pe datele de identitate ca să avem voie să contrazicem
/// utilizatorul.
///
/// Regula, din specul de fix-uri §1: OCR-ul NU blochează. Sub prag (sau fără câmp extras) nu se
/// afișează eroare — se afișează un avertisment, iar dosarul se marchează pentru verificare
/// manuală. Eroarea blocantă rămâne doar când ambele valori sunt citite cu încredere mare și
/// chiar diferă.
/// </summary>
public static class IdentityConfidence
{
    /// <summary>
    /// Pragul recomandat de spec pentru CNP-ul citit din buletin. Mai mare decât pragul general
    /// de review (0.75): o cifră greșită aici respinge un CNP corect, iar costul e un utilizator
    /// blocat degeaba.
    /// </summary>
    public const double IdentityFieldThreshold = 0.85;

    /// <summary>Câmpurile de identitate pentru care pragul de mai sus se aplică.</summary>
    public static readonly string[] IdentityFieldKeys = ["CNP", "DATE_OF_BIRTH"];

    /// <summary>Cheia aparține setului de identitate (comparație case-insensitive).</summary>
    public static bool IsIdentityField(string? fieldKey) =>
        fieldKey is not null
        && IdentityFieldKeys.Contains(fieldKey.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Valoarea citită e destul de sigură cât să putem contrazice ce a tastat utilizatorul.
    /// </summary>
    public static bool IsTrustworthy(double effectiveConfidence) =>
        effectiveConfidence >= IdentityFieldThreshold;
}
