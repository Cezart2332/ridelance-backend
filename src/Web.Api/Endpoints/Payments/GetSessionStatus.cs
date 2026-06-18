using Application.Abstractions.Messaging;
using Application.Payments.GetSessionStatus;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Payments;

internal sealed class GetSessionStatus : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("payments/session-status", async (
            string sessionId,
            IQueryHandler<GetSessionStatusQuery, SessionStatusResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetSessionStatusQuery(sessionId);
            Result<SessionStatusResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .AllowAnonymous()
        .WithTags(Tags.Payments);
    }
}
