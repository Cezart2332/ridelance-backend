namespace Domain.Payments;

/// <summary>
/// Planurile de abonament. Valorile numerice sunt stabile: <c>pending_plan</c> se salvează ca număr,
/// deci un plan nou primește o valoare nouă, nu una a altuia. 0 a fost Solo, 1 Start, 2 Pro.
/// </summary>
public enum SubscriptionPlan
{
    /// <summary>PFAlone: șoferul își ține singur registrele și își generează declarațiile.</summary>
    PfaAlone = 0,

    /// <summary>PFA Full: RIDElance se ocupă de toată partea fiscală, prin contabil.</summary>
    PfaFull = 1,

    Fleet = 3,
}

/// <summary>Opțiunile plătite ale PFAlone. La PFA Full sunt incluse.</summary>
public enum SubscriptionAddon
{
    OpenBanking = 0,
    CashRegister = 1,
}
