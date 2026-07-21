using Application.Abstractions;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Application.Documents.AiVerification;
using Application.Notifications;
using Domain.Documents;
using Domain.Notifications;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Documents.UpdateStatus;

internal sealed class UpdateDocumentStatusCommandHandler(
    IApplicationDbContext context,
    IWebPushService webPushService,
    IEmailService emailService,
    IMjmlRenderer mjmlRenderer,
    IConfiguration configuration)
    : ICommandHandler<UpdateDocumentStatusCommand>
{
    public async Task<Result> Handle(
        UpdateDocumentStatusCommand command,
        CancellationToken cancellationToken)
    {
        Document? document = await context.Documents
            .Include(d => d.User)
            .SingleOrDefaultAsync(d => d.Id == command.DocumentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure(DocumentErrors.NotFound(command.DocumentId));
        }

        User? user = await context.Users
            .SingleOrDefaultAsync(u => u.Id == command.RequestingUserId, cancellationToken);

        if (user is null || user.Role != UserRole.Admin && user.Role != UserRole.Contabil)
        {
            return Result.Failure(DocumentErrors.AccessDenied);
        }

        DocumentStatus previousStatus = document.Status;
        document.Status = command.Status;

        bool notifyRejection = command.Status == DocumentStatus.Rejected &&
                               previousStatus != DocumentStatus.Rejected &&
                               document.UserId != command.RequestingUserId;

        string documentLabel = DocumentAiCatalog.LabelFor(document.Category);

        if (notifyRejection)
        {
            context.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = document.UserId,
                Text = $"Documentul „{documentLabel}” a fost respins de echipa RIDElance. Încarcă o variantă nouă.",
                Type = NotificationTypes.DocumentStatusUpdate,
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        if (notifyRejection)
        {
            await NotifyOwnerAsync(document.User, documentLabel, cancellationToken);
        }

        return Result.Success();
    }

    private async Task NotifyOwnerAsync(User owner, string documentLabel, CancellationToken cancellationToken)
    {
        Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase)
            ? parsedBase
            : null;
        Uri deepLinkUri = appBaseUri is null
            ? new Uri("/app", UriKind.Relative)
            : new Uri(appBaseUri, "/app");
        string deepLink = deepLinkUri.ToString();

        List<PushSubscription> subscriptions = await context.PushSubscriptions
            .Where(s => s.UserId == owner.Id)
            .ToListAsync(cancellationToken);

        foreach (PushSubscription sub in subscriptions)
        {
            try
            {
                await webPushService.SendPushNotificationAsync(
                    sub,
                    "Document respins",
                    $"„{documentLabel}” a fost respins. Încarcă o variantă nouă.",
                    deepLink,
                    cancellationToken);
            }
            catch
            {
                // Ignore push sending failures
            }
        }

        if (string.IsNullOrWhiteSpace(owner.Email))
        {
            return;
        }

        string subject = $"Document respins — {documentLabel}";
        string mjml = EmailTemplates.Notice(
            "Document de reîncărcat",
            $"{owner.FirstName} {owner.LastName}".Trim(),
            [
                $"Documentul „{documentLabel}” încărcat în contul tău RIDElance a fost respins de echipa noastră.",
                "Te rugăm să încarci o variantă corectă a documentului pentru a putea continua verificarea.",
            ],
            null,
            "Încarcă documentul din nou",
            deepLinkUri);

        await emailService.SendEmailAsync(owner.Email, subject, mjmlRenderer.Render(mjml), cancellationToken);
    }
}
