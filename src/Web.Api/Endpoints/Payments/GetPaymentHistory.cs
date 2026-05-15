using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Payments.GetPaymentHistory;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Payments;

internal sealed class GetPaymentHistory : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("payments/history", async (
            IUserContext userContext,
            IQueryHandler<GetPaymentHistoryQuery, List<PaymentHistoryItem>> handler,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            var query = new GetPaymentHistoryQuery(userContext.UserId, page, pageSize);
            Result<List<PaymentHistoryItem>> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Payments);
    }
}
