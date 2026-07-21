using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Application.Abstractions.Services;
using Application.Documents.AiVerification;
using Domain.Documents;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Documents.Upload;

internal sealed class UploadDocumentCommandHandler(
    IApplicationDbContext context,
    IFileEncryptionService fileEncryptionService,
    IWebPushService webPushService,
    IConfiguration configuration)
    : ICommandHandler<UploadDocumentCommand, Guid>
{
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "APPLICATION/PDF",
        "IMAGE/JPEG",
        "IMAGE/PNG"
    ];

    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    public async Task<Result<Guid>> Handle(
        UploadDocumentCommand command,
        CancellationToken cancellationToken)
    {
        if (!AllowedContentTypes.Contains(command.ContentType.ToUpperInvariant()))
        {
            return Result.Failure<Guid>(DocumentErrors.InvalidFileType);
        }

        if (command.FileSize > MaxFileSize)
        {
            return Result.Failure<Guid>(DocumentErrors.FileTooLarge);
        }

        bool aiConfigured = !string.IsNullOrWhiteSpace(
            configuration["OpenRouter:ApiKey"] ?? Environment.GetEnvironmentVariable("OpenRouter__ApiKey"));

        string storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(command.FileName)}";

        EncryptedFileResult encryptionResult = await fileEncryptionService.EncryptAndSaveAsync(
            command.FileStream,
            storedFileName,
            cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            PfaRegistrationId = command.PfaRegistrationId,
            OriginalFileName = command.FileName,
            StoredFileName = storedFileName,
            ContentType = command.ContentType,
            Category = command.Category,
            Status = DocumentStatus.Pending,
            EncryptedFilePath = encryptionResult.FilePath,
            EncryptionIv = encryptionResult.Iv,
            FileSize = command.FileSize,
            UploadedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = command.ExpiresAtUtc,
            AiStatus = aiConfigured && DocumentAiCatalog.IsEligible(command.Category)
                ? DocumentAiStatus.Queued
                : DocumentAiStatus.None
        };

        context.Documents.Add(document);
        await context.SaveChangesAsync(cancellationToken);

        // Find assigned contabil
        Guid? assignedContabilId = null;
        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == command.UserId, cancellationToken);

        if (pfa != null)
        {
            assignedContabilId = pfa.AssignedContabilId;
        }

        if (assignedContabilId.HasValue)
        {
            User? clientUser = await context.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

            string clientName = clientUser != null ? $"{clientUser.FirstName} {clientUser.LastName}" : "Un client";
            string text = $"{clientName} a încărcat documentul: {command.FileName}";

            // In-app notification for the accountant
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = assignedContabilId.Value,
                Text = text,
                Type = NotificationTypes.DocumentUploaded,
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            };
            context.Notifications.Add(notification);
            await context.SaveChangesAsync(cancellationToken);

            // Send push notification
            List<PushSubscription> subscriptions = await context.PushSubscriptions
                .Where(s => s.UserId == assignedContabilId.Value)
                .ToListAsync(cancellationToken);

            string pushTitle = "Document nou";
            string shortFileName = command.FileName.Length > 15 ? command.FileName[..12] + "..." : command.FileName;
            string pushBody = $"Client {clientUser?.FirstName}: {shortFileName}";

            Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase) ? parsedBase : null;
            string relativePath = "/contabil/dashboard";
            string deepLink = appBaseUri is null ? relativePath : new Uri(appBaseUri, relativePath).ToString();

            foreach (PushSubscription sub in subscriptions)
            {
                try
                {
                    await webPushService.SendPushNotificationAsync(sub, pushTitle, pushBody, deepLink, cancellationToken);
                }
                catch
                {
                    // Ignore push sending failures
                }
            }
        }

        return document.Id;
    }
}
