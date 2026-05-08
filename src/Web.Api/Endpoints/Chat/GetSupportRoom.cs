using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Chat.GetSupportRoom;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chat;

internal sealed class GetSupportRoom : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("chat/support-room", async (
            IUserContext userContext,
            ICommandHandler<GetSupportRoomCommand, GetSupportRoomResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new GetSupportRoomCommand(userContext.UserId);

            Result<GetSupportRoomResponse> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Chat);
    }
}
