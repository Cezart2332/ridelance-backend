namespace Domain.Payments;

public sealed class ServiceOrder
{
    public Guid Id { get; set; }
    public string ServiceKey { get; set; } = string.Empty;
    public string ServiceTitle { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public ServiceOrderStatus Status { get; set; } = ServiceOrderStatus.Pending;
    public string? StripeSessionId { get; set; }
    public long? AmountBani { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAtUtc { get; set; }
}

public enum ServiceOrderStatus
{
    Pending,
    Paid,
}
