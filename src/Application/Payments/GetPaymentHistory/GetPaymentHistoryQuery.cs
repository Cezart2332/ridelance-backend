using Application.Abstractions.Messaging;

namespace Application.Payments.GetPaymentHistory;

public sealed record GetPaymentHistoryQuery(Guid UserId, int Page = 1, int PageSize = 20)
    : IQuery<List<PaymentHistoryItem>>;

public sealed record PaymentHistoryItem(
    Guid Id,
    string PaymentType,
    string Status,
    long AmountBani,
    string Description,
    string? StripePaymentId,
    DateTime CreatedAtUtc);
