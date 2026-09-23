using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Chat.GetMessages;
using Domain.Chat;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Chat.Attachments;

/// <summary>Un fișier (poză, PDF, document) trimis în chat, cu un mesaj opțional.</summary>
public sealed record SendChatAttachmentCommand(
    Guid ChatRoomId,
    Guid SenderId,
    string FileName,
    string ContentType,
    Stream FileStream,
    long FileSize,
    string? Caption) : ICommand<ChatMessageDto>;

public sealed record DownloadChatAttachmentQuery(Guid MessageId, Guid RequestingUserId) : IQuery<ChatAttachmentFile>;

public sealed record ChatAttachmentFile(Stream FileStream, string ContentType, string FileName);

public static class ChatAttachmentRules
{
    // Ca la documente: pozele făcute cu telefonul trec ușor de 10 MB. Kestrel acceptă ~30 MB pe cerere.
    public const long MaxFileSize = 25 * 1024 * 1024;

    public const int MaxCaptionLength = 4096;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "image/heic",
        "image/heif",
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/csv",
        "text/plain",
    };

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".heic"] = "image/heic",
        [".heif"] = "image/heif",
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".csv"] = "text/csv",
        [".txt"] = "text/plain",
    };

    public static bool IsAllowed(string contentType) => AllowedContentTypes.Contains(contentType);

    /// <summary>
    /// Tipul fișierului: cel trimis de browser sau, când lipsește ori e generic (HEIC pe Windows,
    /// CSV deschis cu Excel), cel după extensie.
    /// </summary>
    public static string ResolveContentType(string? contentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType) && IsAllowed(contentType))
        {
            return contentType;
        }

        return ByExtension.TryGetValue(Path.GetExtension(fileName), out string? byExtension)
            ? byExtension
            : contentType ?? string.Empty;
    }

    public static readonly Error InvalidFileType = Error.Problem(
        "Chat.InvalidFileType",
        "Poți trimite poze (JPG, PNG, WEBP, HEIC), PDF, Word, Excel, CSV sau text.");

    public static readonly Error FileTooLarge = Error.Problem("Chat.FileTooLarge", "Fișierul e prea mare. Limita e 25 MB.");

    public static readonly Error EmptyFile = Error.Problem("Chat.EmptyFile", "Fișierul e gol.");

    public static readonly Error CaptionTooLong = Error.Problem("Chat.CaptionTooLong", "Mesajul e prea lung.");

    public static Error AttachmentNotFound(Guid messageId) =>
        Error.NotFound("Chat.AttachmentNotFound", $"Mesajul {messageId} nu are un fișier atașat.");
}

internal sealed class SendChatAttachmentCommandHandler(
    IApplicationDbContext context,
    IFileEncryptionService encryption)
    : ICommandHandler<SendChatAttachmentCommand, ChatMessageDto>
{
    public async Task<Result<ChatMessageDto>> Handle(SendChatAttachmentCommand command, CancellationToken cancellationToken)
    {
        string contentType = ChatAttachmentRules.ResolveContentType(command.ContentType, command.FileName);
        if (!ChatAttachmentRules.IsAllowed(contentType))
        {
            return Result.Failure<ChatMessageDto>(ChatAttachmentRules.InvalidFileType);
        }

        if (command.FileSize <= 0)
        {
            return Result.Failure<ChatMessageDto>(ChatAttachmentRules.EmptyFile);
        }

        if (command.FileSize > ChatAttachmentRules.MaxFileSize)
        {
            return Result.Failure<ChatMessageDto>(ChatAttachmentRules.FileTooLarge);
        }

        string caption = command.Caption?.Trim() ?? string.Empty;
        if (caption.Length > ChatAttachmentRules.MaxCaptionLength)
        {
            return Result.Failure<ChatMessageDto>(ChatAttachmentRules.CaptionTooLong);
        }

        ChatRoom? room = await context.ChatRooms.SingleOrDefaultAsync(r => r.Id == command.ChatRoomId, cancellationToken);
        if (room is null)
        {
            return Result.Failure<ChatMessageDto>(ChatErrors.RoomNotFound(command.ChatRoomId));
        }

        User? sender = await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == command.SenderId, cancellationToken);
        if (sender is null || !await ChatAccess.IsParticipantAsync(context, room, sender, cancellationToken))
        {
            return Result.Failure<ChatMessageDto>(ChatErrors.AccessDenied);
        }

        string fileName = Path.GetFileName(command.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "fisier";
        }

        if (fileName.Length > 255)
        {
            fileName = fileName[^255..];
        }

        EncryptedFileResult stored = await encryption.EncryptAndSaveAsync(
            command.FileStream,
            $"chat-{Guid.NewGuid()}{Path.GetExtension(fileName)}",
            cancellationToken);

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatRoomId = room.Id,
            SenderId = sender.Id,
            Content = caption,
            SentAtUtc = DateTime.UtcNow,
            IsRead = false,
            AttachmentFileName = fileName,
            AttachmentContentType = contentType,
            AttachmentSize = command.FileSize,
            AttachmentPath = stored.FilePath,
            AttachmentIv = stored.Iv,
        };

        room.LastMessageAtUtc = message.SentAtUtc;
        context.ChatMessages.Add(message);
        await context.SaveChangesAsync(cancellationToken);

        return new ChatMessageDto(
            message.Id,
            sender.Id,
            ChatSenderName.For(sender),
            sender.Role.ToString(),
            message.Content,
            message.SentAtUtc,
            false,
            new ChatAttachmentDto(fileName, contentType, command.FileSize));
    }
}

internal sealed class DownloadChatAttachmentQueryHandler(
    IApplicationDbContext context,
    IFileEncryptionService encryption)
    : IQueryHandler<DownloadChatAttachmentQuery, ChatAttachmentFile>
{
    public async Task<Result<ChatAttachmentFile>> Handle(DownloadChatAttachmentQuery query, CancellationToken cancellationToken)
    {
        ChatMessage? message = await context.ChatMessages
            .AsNoTracking()
            .Include(m => m.ChatRoom)
            .Include(m => m.Sender)
            .SingleOrDefaultAsync(m => m.Id == query.MessageId, cancellationToken);

        if (message?.AttachmentPath is null || message.AttachmentIv is null)
        {
            return Result.Failure<ChatAttachmentFile>(ChatAttachmentRules.AttachmentNotFound(query.MessageId));
        }

        User? user = await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == query.RequestingUserId, cancellationToken);
        if (user is null || !await ChatAccess.IsParticipantAsync(context, message.ChatRoom, user, cancellationToken))
        {
            return Result.Failure<ChatAttachmentFile>(ChatErrors.AccessDenied);
        }

        // Contabilul nu vede mesajele adminului din camera clientului — nici fișierele lor.
        if (user.Role == UserRole.Contabil && message.Sender.Role == UserRole.Admin)
        {
            return Result.Failure<ChatAttachmentFile>(ChatErrors.AccessDenied);
        }

        Stream stream = await encryption.DecryptAndReadAsync(message.AttachmentPath, message.AttachmentIv, cancellationToken);
        return new ChatAttachmentFile(stream, message.AttachmentContentType ?? "application/octet-stream", message.AttachmentFileName ?? "fisier");
    }
}
