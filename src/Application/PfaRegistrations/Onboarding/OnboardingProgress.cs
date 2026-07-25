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
    /// <summary>
    /// Marchează dosarul ca înrolat dacă (și doar dacă) e prima dată când TOȚI cei 6 pași sunt
    /// finalizați (nu doar secțiunile de documente). Întoarce <c>true</c> exact la tranziția spre
    /// „înrolat", ca apelantul să declanșeze notificarea/emailul de înrolare o singură dată.
    /// Necesită un <paramref name="registration"/> cu navele încărcate (fiscal, bancă, Oblio, ARR,
    /// platforme, vehicule) + profilul de eligibilitate.
    /// </summary>
    public static bool TryMarkCompleted(
        PfaRegistration registration,
        OnboardingEligibilityProfile? eligibility,
        DateTime nowUtc)
    {
        if (registration.OnboardingCompletedAtUtc is not null)
        {
            return false;
        }

        if (registration.Status != PfaRegistrationStatus.Approved)
        {
            return false;
        }

        // Status == Approved ⇒ pasul PFA e Validat; derivăm ceilalți pași din entități.
        List<OnboardingStepDto> steps = OnboardingStepCatalog.BuildSteps(
            registration, OnboardingSectionStatus.Validated, eligibility);

        if (!OnboardingStepCatalog.AllCompleted(steps))
        {
            return false;
        }

        registration.OnboardingCompletedAtUtc = nowUtc;
        return true;
    }
}
