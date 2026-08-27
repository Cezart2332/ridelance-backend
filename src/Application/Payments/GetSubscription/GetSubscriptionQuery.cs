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
    DateTime? BcrDiscountConfirmedAtUtc = null);
