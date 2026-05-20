using System.Diagnostics.CodeAnalysis;
using Application.Abstractions.Messaging;

namespace Application.Payments.CreatePublicServiceCheckout;

public sealed record CreatePublicServiceCheckoutCommand(
    string ServiceKey,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    [property: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "API DTO")]
    [param: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "API DTO")]
    string? SuccessUrl = null,
    [property: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "API DTO")]
    [param: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "API DTO")]
    string? CancelUrl = null
) : ICommand<string>;
