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

    /// <summary>
    /// Abonamentele. Apare în sidebar sub aceleași reguli de deblocare ca restul, dar conținutul
    /// ei nu e definit încă, deci nu se poate finaliza — și tocmai de aceea NU intră în
    /// <c>AllCompleted</c>: altfel nimeni nu ar mai termina onboardingul.
    /// </summary>
    Subscriptions = 6,
}
