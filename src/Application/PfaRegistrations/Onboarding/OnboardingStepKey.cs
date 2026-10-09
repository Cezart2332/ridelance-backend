namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Cei 4 pași ai onboardingului, ca identitate tipizată. Până acum pașii existau doar ca string-uri
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
    /// <summary>
    /// ARR &amp; Cont Flotă: înlocuiește fostele trei (autorizația, Uber &amp; Bolt, vehiculul). Clientul
    /// încarcă și plătește, restul procedurii o face agentul din admin.
    /// </summary>
    ArrFleet = 3,
}
