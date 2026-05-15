using Application.Abstractions.Messaging;
using Domain.Payments;

namespace Application.Payments.GetSubscription;

public sealed record GetSubscriptionQuery(Guid UserId) : IQuery<SubscriptionResponse?>;

public sealed record SubscriptionResponse(
    Guid Id,
    string Plan,
    string Status,
    string? StripeSubscriptionId,
    DateTime FirstBillingDateUtc,
    DateTime? NextBillingDateUtc,
    DateTime CreatedAtUtc,
    bool DashboardAccessGranted);
