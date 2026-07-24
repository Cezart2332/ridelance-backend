namespace Domain.PfaRegistrations;

/// <summary>
/// Tipul de înregistrare în scopuri de TVA. Specificația cere să NU hard-codăm
/// „TVA intracomunitar" — se determină controlat, cu verificare.
/// </summary>
public enum VatRegistrationKind
{
    None = 0,
    /// <summary>Cod special conform art. 317 (intracomunitar, fără plată TVA în țară).</summary>
    SpecialArticle317 = 1,
    /// <summary>Plătitor de TVA obișnuit.</summary>
    StandardVat = 2,
    Unknown = 3,
}
