using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Motorul de progres al onboardingului. Singurul loc care decide când un dosar PFA
/// devine „înrolat": abia când toate secțiunile obligatorii de documente sunt validate,
/// nu la aprobarea dosarului PFA. Aprobarea PFA (<see cref="PfaRegistrationStatus.Approved"/>)
/// înseamnă doar „dosar PFA verificat", nu „înrolat".
/// </summary>
public static class OnboardingProgress
{
    /// <summary>Secțiunile de documente care trebuie validate pentru a finaliza onboardingul.</summary>
    public static readonly OnboardingSectionKey[] RequiredDocumentSections =
    [
        OnboardingSectionKey.AutorizatieTransport,
        OnboardingSectionKey.CopieConforma,
        OnboardingSectionKey.Vehicul,
    ];

    /// <summary>
    /// Marchează dosarul ca înrolat dacă (și doar dacă) e prima dată când toate condițiile
    /// sunt îndeplinite. Întoarce <c>true</c> exact la tranziția spre „înrolat", ca apelantul
    /// să declanșeze notificarea/emailul de înrolare o singură dată.
    /// </summary>
    public static bool TryMarkCompleted(PfaRegistration registration, DateTime nowUtc)
    {
        if (registration.OnboardingCompletedAtUtc is not null)
        {
            return false;
        }

        if (registration.Status != PfaRegistrationStatus.Approved)
        {
            return false;
        }

        bool allValidated = RequiredDocumentSections.All(key =>
            registration.OnboardingSections.Any(s =>
                s.SectionKey == key && s.Status == OnboardingSectionStatus.Validated));

        if (!allValidated)
        {
            return false;
        }

        registration.OnboardingCompletedAtUtc = nowUtc;
        return true;
    }
}
