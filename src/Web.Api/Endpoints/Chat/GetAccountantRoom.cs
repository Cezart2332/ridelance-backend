using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Chat.GetAccountantRoom;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chat;

internal sealed class GetAccountantRoom : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("chat/accountant-room", async (
            IUserContext userContext,
            ICommandHandler<GetAccountantRoomCommand, GetAccountantRoomResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new GetAccountantRoomCommand(userContext.UserId);

            Result<GetAccountantRoomResponse> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Chat);
    }
}
