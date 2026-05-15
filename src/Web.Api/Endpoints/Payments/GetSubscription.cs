using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Payments.GetSubscription;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Payments;

internal sealed class GetSubscription : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("payments/subscription", async (
            IUserContext userContext,
            IQueryHandler<GetSubscriptionQuery, SubscriptionResponse?> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetSubscriptionQuery(userContext.UserId);
            Result<SubscriptionResponse?> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Payments);
    }
}
