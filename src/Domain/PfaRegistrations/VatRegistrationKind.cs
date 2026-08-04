namespace Domain.PfaRegistrations;

/// <summary>
/// Tipul de înregistrare în scopuri de TVA. Nu se cere de la client — se derivă din
/// răspunsul la întrebarea despre codul special de TVA intracomunitar (art. 317),
/// care e însoțit de dovadă.
/// </summary>
public enum VatRegistrationKind
{
    None = 0,
    /// <summary>Cod special conform art. 317 (intracomunitar, fără plată TVA în țară).</summary>
    SpecialArticle317 = 1,
    /// <summary>Plătitor de TVA obișnuit. Se stabilește din panoul fiscal, nu din onboarding.</summary>
    StandardVat = 2,
    /// <summary>Valoare istorică („nu sunt sigur”). Nu mai poate fi scrisă din onboarding.</summary>
    Unknown = 3,
}
