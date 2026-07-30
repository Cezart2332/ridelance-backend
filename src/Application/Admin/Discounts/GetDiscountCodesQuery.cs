using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Payments;
using SharedKernel;

namespace Application.Admin.Discounts;

public sealed record GetDiscountCodesQuery : IQuery<IReadOnlyList<DiscountCode>>;

internal sealed class GetDiscountCodesQueryHandler(IStripeService stripeService)
    : IQueryHandler<GetDiscountCodesQuery, IReadOnlyList<DiscountCode>>
{
    public async Task<Result<IReadOnlyList<DiscountCode>>> Handle(
        GetDiscountCodesQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DiscountCode> codes = await stripeService.ListDiscountCodesAsync(cancellationToken);
        return Result.Success(codes);
    }
}
