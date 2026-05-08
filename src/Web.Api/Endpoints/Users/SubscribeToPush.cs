using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Users.SubscribeToPush;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class SubscribeToPush : IEndpoint
{
    public sealed class Request
    {
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
    }

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/push/subscribe", async (
            Request request,
            IUserContext userContext,
            ICommandHandler<SubscribeToPushCommand> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new SubscribeToPushCommand(
                userContext.UserId,
                request.Endpoint,
                request.P256dh,
                request.Auth);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Users);
    }
}
