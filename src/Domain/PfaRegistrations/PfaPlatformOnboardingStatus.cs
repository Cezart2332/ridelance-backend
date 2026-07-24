namespace Domain.PfaRegistrations;

/// <summary>
/// Statusul de onboarding al unui cont de operator (Uber/Bolt) la Pasul 4.
/// Avans manual din admin până apar integrările cu platformele.
/// </summary>
public enum PfaPlatformOnboardingStatus
{
    NotStarted = 0,
    /// <summary>Userul a selectat platforma.</summary>
    Selected = 1,
    /// <summary>Contul de operator e legat/creat.</summary>
    AccountLinked = 2,
    /// <summary>Contractul de afiliere e semnat.</summary>
    ContractSigned = 3,
    /// <summary>Activ pe platformă.</summary>
    Active = 4,
    /// <summary>Userul nu folosește această platformă.</summary>
    Skipped = 5,
}
