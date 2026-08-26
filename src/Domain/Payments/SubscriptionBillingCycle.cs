namespace Domain.Payments;

/// <summary>
/// Cum se facturează un abonament. Până acum exista un singur ciclu — săptămânal, luni la 15:00 —
/// deci nu avea ce alege nimeni. De când planurile sunt lunare cu variantă anuală, ciclul e o
/// decizie a clientului la checkout și trebuie ținut minte: reînnoirea, prețul afișat și
/// descrierea de pe factură se derivă din el.
/// </summary>
public enum SubscriptionBillingCycle
{
    Monthly,
    Annual,
}
