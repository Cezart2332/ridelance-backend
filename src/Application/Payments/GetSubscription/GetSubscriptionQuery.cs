using Application.Abstractions.Messaging;
using Domain.Payments;

namespace Application.Payments.GetSubscription;

public sealed record GetSubscriptionQuery(Guid UserId) : IQuery<SubscriptionResponse?>;

public sealed record SubscriptionResponse(
    Guid? Id,
    string? Plan,
    string? Status,
    string? StripeSubscriptionId,
    DateTime? FirstBillingDateUtc,
    DateTime? NextBillingDateUtc,
    DateTime? CreatedAtUtc,
    bool DashboardAccessGranted,
    // "Monthly" | "Annual". Null pe răspunsul fără abonament.
    string? BillingCycle = null,
    string? PfaStatus = null,
    string? PfaRegistrationType = null,
    string? PendingPlan = null,
    bool HasPaidInfiintare = false,
    bool OnboardingSectionsValidated = false,
    /// <summary>Clientul a bifat contul BCR la checkout. Doar intenția.</summary>
    bool BcrDiscountRequested = false,
    /// <summary>Când a confirmat BCR contul. De aici curg cele șase luni de reducere.</summary>
    DateTime? BcrDiscountConfirmedAtUtc = null,
    /// <summary>Cheia planului, ca în oferta publică: „pfalone”, „pfa-full”, „fleet”.</summary>
    string? PlanKey = null,
    /// <summary>Conectarea băncii: inclusă în PFA Full, plătită separat la PFAlone.</summary>
    bool IncludesOpenBanking = false,
    /// <summary>Automatizarea casei de marcat: inclusă în PFA Full, plătită separat la PFAlone.</summary>
    bool IncludesCashRegister = false,
    /// <summary>PFAlone: își ține singur registrele și le vede.</summary>
    bool CanManageRegisters = false,
    /// <summary>PFAlone: are generatorul de declarații.</summary>
    bool CanGenerateDeclarations = false,
    /// <summary>PFAlone: până când Open Banking e gratuit.</summary>
    DateTime? OpenBankingTrialEndsAtUtc = null,
    /// <summary>PFAlone: luna gratuită s-a terminat; alege dacă plătește Open Banking sau renunță.</summary>
    bool OpenBankingDecisionDue = false,
    /// <summary>Opțiunile plătite pe abonament (nu cele incluse sau gratuite).</summary>
    bool HasOpenBankingAddon = false,
    bool HasCashRegisterAddon = false);

/// <summary>Cheia unui plan, aceeași ca în oferta publică și la checkout.</summary>
public static class PlanKeys
{
    public const string PfaAlone = "pfalone";
    public const string PfaFull = "pfa-full";
    public const string Fleet = "fleet";

    public static string Of(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.PfaAlone => PfaAlone,
        SubscriptionPlan.PfaFull => PfaFull,
        _ => Fleet,
    };
}
