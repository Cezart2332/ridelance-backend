using System.Diagnostics.CodeAnalysis;
using Application.Abstractions.Messaging;

namespace Application.Payments.CreateCarListingCheckout;

public sealed record CreateCarListingCheckoutCommand(
    Guid UserId,
    string UserEmail,
    Guid CarId,
    [property: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "Strings are preferred for API DTOs")]
    [param: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "Strings are preferred for API DTOs")]
    string? SuccessUrl = null,
    [property: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "Strings are preferred for API DTOs")]
    [param: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "Strings are preferred for API DTOs")]
    string? CancelUrl = null) : ICommand<string>;
