using SharedKernel;

namespace Domain.PfaRegistrations;

public static class OnboardingErrors
{
    public static readonly Error SectionNotFound = Error.NotFound(
        "Onboarding.SectionNotFound",
        "Secțiunea de onboarding nu a fost găsită.");

    public static readonly Error SectionLocked = Error.Problem(
        "Onboarding.SectionLocked",
        "Secțiunea nu este încă deblocată. Finalizează secțiunile anterioare mai întâi.");

    public static readonly Error SectionNotSubmittable = Error.Problem(
        "Onboarding.SectionNotSubmittable",
        "Secțiunea nu poate fi trimisă la validare în starea curentă.");

    public static Error MissingDocuments(string categories) => Error.Problem(
        "Onboarding.MissingDocuments",
        $"Lipsesc documente obligatorii: {categories}.");

    public static readonly Error PfaSectionManagedViaRegistration = Error.Problem(
        "Onboarding.PfaSectionManagedViaRegistration",
        "Secțiunea PFA se validează prin aprobarea dosarului PFA.");

    public static readonly Error NotAwaitingValidation = Error.Problem(
        "Onboarding.NotAwaitingValidation",
        "Secțiunea nu se află în validare.");

    /// <summary>
    /// Scriere pe un pas care nu e cel activ. Conflict (409), nu Problem (400): cererea e validă în
    /// sine, doar starea dosarului nu o permite — un client care reîncearcă după ce pasul se
    /// deblochează va reuși cu exact același payload.
    /// </summary>
    public static Error StepNotActive(string label) => Error.Conflict(
        "Onboarding.StepNotActive",
        $"Pasul „{label}” nu este activ acum. Finalizează pasul curent înainte să continui.");
}
