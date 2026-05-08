using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Chat.GetOrCreateRoom;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chat;

internal sealed class GetOrCreateRoom : IEndpoint
{
    public sealed record Request(Guid TargetUserId);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("chat/rooms", async (
            Request request,
            IUserContext userContext,
            ICommandHandler<GetOrCreateRoomCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new GetOrCreateRoomCommand(request.TargetUserId, userContext.UserId);

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(
                roomId => Results.Ok(new { roomId }),
                CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Chat);
    }
}
