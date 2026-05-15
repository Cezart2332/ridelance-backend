namespace Domain.Payments;

public enum PaymentType
{
    OneTime,
    Subscription
}

public enum PaymentStatus
{
    Succeeded,
    Pending,
    Failed,
    Refunded
}
