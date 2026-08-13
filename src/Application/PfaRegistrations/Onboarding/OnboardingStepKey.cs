namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Cei 6 pași ai onboardingului, ca identitate tipizată. Până acum pașii existau doar ca string-uri
/// într-un array, ceea ce însemna că un guard „scrii pe pasul corect?” nu avea de ce să se lege.
///
/// Valoarea numerică ESTE ordinea în flux, iar fluxul e liniar: pasul N se deblochează doar când
/// N-1 e finalizat. Nu adăuga valori la mijloc fără să muți și dependențele.
/// </summary>
public enum OnboardingStepKey
{
    Eligibility = 0,
    Pfa = 1,
    Fiscal = 2,
    Arr = 3,
    Platforms = 4,
    Vehicle = 5,
}
