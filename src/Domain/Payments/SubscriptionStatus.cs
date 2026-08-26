namespace Domain.Payments;

public enum SubscriptionStatus
{
    /// <summary>
    /// Subscription is active and paid. Starea normală de la prima plată încolo.
    /// </summary>
    Active,

    /// <summary>
    /// Istoric: „plătit, dar prima încasare automată e abia lunea viitoare”. Nu se mai scrie
    /// nicăieri — încasarea se face acum, la checkout. Membrul rămâne pentru că statusul e stocat
    /// ca text, iar rândurile scrise înainte de schimbare încă poartă valoarea asta; e tratată
    /// peste tot ca <see cref="Active"/>.
    /// </summary>
    ActivePendingBilling,

    /// <summary>
    /// Subscription was cancelled.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Payment failed; subscription is past due.
    /// </summary>
    PastDue,

    /// <summary>
    /// Subscription has expired.
    /// </summary>
    Expired,

    /// <summary>
    /// Istoric: „plătit, dar accesul se acordă la următoarea rulare de luni 15:00”. Poarta a
    /// dispărut odată cu jobul; ca și <see cref="ActivePendingBilling"/>, membrul rămâne doar
    /// pentru rândurile vechi.
    /// </summary>
    PaidPendingAccess
}
