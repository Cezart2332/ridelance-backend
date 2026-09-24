using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Chat;
using Application.Chat.GetMessages;
using Application.Chat.SendMessage;
using Infrastructure.Chat;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Chat;

/// <summary>
/// Trimiterea unui mesaj text prin HTTP, lângă cea prin SignalR.
///
/// Pe telefon conexiunea în timp real cade des (rețea mobilă, aplicație trecută în fundal), iar
/// textul pleca doar pe ea: chatul se încărca, dar mesajul nu mai pleca. Aplicația încearcă întâi
/// SignalR și, dacă nu e conectată, trimite pe aici. Aceeași comandă ca în hub, deci aceleași
/// verificări de acces, și mesajul ajunge live în cameră ca oricare altul.
/// </summary>
internal sealed class SendMessage : IEndpoint
{
    public sealed record Request(string Content);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("chat/rooms/{roomId:guid}/messages", async (
            Guid roomId,
            Request request,
            IUserContext userContext,
            IApplicationDbContext context,
            ICommandHandler<SendMessageCommand, Guid> handler,
            ChatMessageNotifier notifier,
            CancellationToken cancellationToken) =>
        {
            string content = request.Content?.Trim() ?? string.Empty;
            Result<Guid> result = await handler.Handle(
                new SendMessageCommand(roomId, userContext.UserId, content), cancellationToken);
            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            Domain.Users.User? sender = await context.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

            var message = new ChatMessageDto(
                result.Value,
                userContext.UserId,
                sender is null ? "Unknown" : ChatSenderName.For(sender),
                sender?.Role.ToString() ?? string.Empty,
                content,
                DateTime.UtcNow,
                false);

            await notifier.PublishAsync(roomId, message, cancellationToken);
            return Results.Ok(message);
        })
        .RequireAuthorization()
        .WithTags(Tags.Chat);
    }
}
