using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Payments;
using SharedKernel;

namespace Application.Admin.Discounts;

/// <summary>
/// Enables or disables an existing discount code. Stripe codes cannot be deleted.
/// </summary>
public sealed record SetDiscountCodeActiveCommand(string PromotionCodeId, bool Active)
    : ICommand<DiscountCode>;

internal sealed class SetDiscountCodeActiveCommandHandler(IStripeService stripeService)
    : ICommandHandler<SetDiscountCodeActiveCommand, DiscountCode>
{
    public async Task<Result<DiscountCode>> Handle(
        SetDiscountCodeActiveCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.PromotionCodeId))
        {
            return Result.Failure<DiscountCode>(Error.Problem(
                "Discount.IdMissing",
                "Codul de reducere nu a fost identificat."));
        }

        DiscountCode updated = await stripeService.SetDiscountCodeActiveAsync(
            command.PromotionCodeId,
            command.Active,
            cancellationToken);

        return Result.Success(updated);
    }
}
