using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Chat.Attachments;
using Application.Chat.GetMessages;
using Infrastructure.Chat;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chat;

/// <summary>
/// Fișiere în chat: se încarcă prin HTTP (SignalR nu e făcut pentru fișiere mari), apoi mesajul
/// ajunge live în cameră ca oricare altul. Descărcarea verifică din nou cine are voie.
/// </summary>
internal sealed class Attachments : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("chat/rooms/{roomId:guid}/attachments", async (
            Guid roomId,
            [FromForm] IFormFile file,
            [FromForm] string? caption,
            IUserContext userContext,
            ICommandHandler<SendChatAttachmentCommand, ChatMessageDto> handler,
            ChatMessageNotifier notifier,
            CancellationToken cancellationToken) =>
        {
            using Stream stream = file.OpenReadStream();
            var command = new SendChatAttachmentCommand(
                roomId,
                userContext.UserId,
                file.FileName,
                file.ContentType,
                stream,
                file.Length,
                caption);

            Result<ChatMessageDto> result = await handler.Handle(command, cancellationToken);
            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            await notifier.PublishAsync(roomId, result.Value, cancellationToken);
            return Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .DisableAntiforgery()
        .WithTags(Tags.Chat);

        app.MapGet("chat/messages/{messageId:guid}/attachment", async (
            Guid messageId,
            IUserContext userContext,
            IQueryHandler<DownloadChatAttachmentQuery, ChatAttachmentFile> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ChatAttachmentFile> result = await handler.Handle(new DownloadChatAttachmentQuery(messageId, userContext.UserId), cancellationToken);
            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            return Results.File(result.Value.FileStream, result.Value.ContentType, result.Value.FileName);
        })
        .RequireAuthorization()
        .WithTags(Tags.Chat);
    }
}
