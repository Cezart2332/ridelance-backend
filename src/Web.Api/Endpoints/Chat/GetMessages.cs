using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Chat.GetMessages;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chat;

internal sealed class GetMessages : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("chat/rooms/{roomId:guid}/messages", async (
            Guid roomId,
            int? page,
            int? pageSize,
            IUserContext userContext,
            IQueryHandler<GetMessagesQuery, ChatMessageListResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetMessagesQuery(
                roomId,
                userContext.UserId,
                page ?? 1,
                pageSize ?? 50);

            Result<ChatMessageListResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Chat);
    }
}
