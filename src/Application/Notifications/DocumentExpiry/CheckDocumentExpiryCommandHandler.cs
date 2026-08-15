using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Application.Documents.Expiry;
using Domain.Documents;
using Domain.Notifications;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Application.Notifications.DocumentExpiry;

/// <summary>
/// Checks all documents with an expiry date and sends notifications when
/// the document expires in exactly 30 days or 7 days (Romania time).
/// Idempotent: a notification is only created if none of the same type
/// exists for this document in the current calendar day.
/// </summary>
internal sealed class CheckDocumentExpiryCommandHandler(
    IApplicationDbContext context,
    IWebPushService webPushService,
    IConfiguration configuration,
    ILogger<CheckDocumentExpiryCommandHandler> logger)
    : ICommandHandler<CheckDocumentExpiryCommand>
{
    // Ce categorii expiră: o singură listă, în DocumentExpiryPolicy. Copia de aici a fost
    // ștearsă — două liste care trebuie ținute sincron sunt două liste care se desincronizează.
    private static readonly IReadOnlySet<DocumentCategory> ExpirableCategories =
        DocumentExpiryPolicy.ExpirableCategories;

    // Notify at these days-before-expiry windows
    private static readonly int[] NotifyAtDaysBefore = [30, 7];

    public async Task<Result> Handle(
        CheckDocumentExpiryCommand command,
        CancellationToken cancellationToken)
    {
        var romania = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        DateTime nowRomania = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, romania);
        DateTime todayRomania = nowRomania.Date;

        // Fetch documents that have an expiry date and belong to expirable categories
        List<Document> documents = await context.Documents
            .AsNoTracking()
            .Where(d =>
                d.ExpiresAtUtc.HasValue &&
                ExpirableCategories.Contains(d.Category))
            .ToListAsync(cancellationToken);

        int notifsSent = 0;

        foreach (Document doc in documents)
        {
            DateTime expiryRomania = TimeZoneInfo
                .ConvertTimeFromUtc(doc.ExpiresAtUtc!.Value, romania)
                .Date;

            int daysUntilExpiry = (expiryRomania - todayRomania).Days;

            if (!NotifyAtDaysBefore.Contains(daysUntilExpiry))
            {
                continue;
            }

            // Idempotency: skip if we already sent this notification today
            string notifTag = $"expiry:{doc.Id}:{daysUntilExpiry}d:{todayRomania:yyyy-MM-dd}";
            bool alreadySent = await context.Notifications
                .AsNoTracking()
                .AnyAsync(n => n.Text.Contains(notifTag), cancellationToken);

            if (alreadySent)
            {
                continue;
            }

            Guid ownerId = doc.UserId;

            // Find assigned contabil
            Guid? contabilId = await context.PfaRegistrations
                .AsNoTracking()
                .Where(p => p.UserId == ownerId)
                .Select(p => p.AssignedContabilId)
                .FirstOrDefaultAsync(cancellationToken);

            string categoryLabel = doc.Category switch
            {
                DocumentCategory.Buletin => "Buletin / CI",
                DocumentCategory.CarteIdentitate => "Carte de identitate",
                DocumentCategory.AsigurareCalatori => "Asigurare Călători",
                DocumentCategory.ITP => "ITP",
                DocumentCategory.Talon => "Talon / certificat de înmatriculare",
                DocumentCategory.RCA => "Poliță RCA",
                DocumentCategory.PermisConducere => "Permis de Conducere",
                DocumentCategory.CopieConforma => "Copie conformă",
                DocumentCategory.EcusonUber => "Ecuson Uber",
                DocumentCategory.EcusonBolt => "Ecuson Bolt",
                DocumentCategory.ContractVehicul => "Contract vehicul",
                _ => doc.Category.ToString()
            };

            string ownerText = daysUntilExpiry == 0
                ? $"Documentul tău \"{categoryLabel}\" a expirat astăzi! [{notifTag}]"
                : $"Documentul tău \"{categoryLabel}\" expiră în {daysUntilExpiry} zile ({expiryRomania:dd.MM.yyyy}). [{notifTag}]";

            // Preferința titularului se respectă doar pentru el: contabila primește oricum
            // anunțul, pentru că e o obligație de serviciu, nu o notificare de confort.
            if (await OwnerWantsAsync(ownerId, NotificationTypes.DocumentExpiringSoon, cancellationToken))
            {
                await CreateNotificationAsync(ownerId, ownerText, NotificationTypes.DocumentExpiringSoon);
            }

            if (contabilId.HasValue)
            {
                User? ownerUser = await context.Users
                    .AsNoTracking()
                    .Where(u => u.Id == ownerId)
                    .FirstOrDefaultAsync(cancellationToken);

                string ownerName = ownerUser != null
                    ? $"{ownerUser.FirstName} {ownerUser.LastName}"
                    : "Un client";

                string contabilText = daysUntilExpiry == 0
                    ? $"Clientul {ownerName}: \"{categoryLabel}\" a expirat astăzi! [{notifTag}:contabil]"
                    : $"Clientul {ownerName}: \"{categoryLabel}\" expiră în {daysUntilExpiry} zile ({expiryRomania:dd.MM.yyyy}). [{notifTag}:contabil]";

                await CreateNotificationAsync(contabilId.Value, contabilText, NotificationTypes.DocumentExpiringSoon);
            }

            await context.SaveChangesAsync(cancellationToken);

            // Push notifications
            Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase) ? parsedBase : null;
            string pushTitle = daysUntilExpiry == 0 ? "Document expirat!" : $"Document expiră în {daysUntilExpiry} zile";

            await SendPushAsync(ownerId, pushTitle, categoryLabel, "/dashboard", appBaseUri, cancellationToken);
            if (contabilId.HasValue)
            {
                await SendPushAsync(contabilId.Value, pushTitle, categoryLabel, "/contabil/dashboard", appBaseUri, cancellationToken);
            }

            notifsSent++;
            logger.LogInformation(
                "Expiry notification sent for document {DocId} ({Category}), {Days} days left.",
                doc.Id, doc.Category, daysUntilExpiry);
        }

        logger.LogInformation("Document expiry check complete. Notifications sent: {Count}", notifsSent);
        return Result.Success();
    }

    /// <summary>
    /// Absența unui rând înseamnă „activ": cine nu s-a atins de setări primește tot. Doar un
    /// „oprit" explicit taie notificarea.
    /// </summary>
    private async Task<bool> OwnerWantsAsync(Guid userId, string notificationType, CancellationToken cancellationToken)
    {
        NotificationCategory? category = NotificationPreference.CategoryForType(notificationType);
        if (category is null)
        {
            return true;
        }

        NotificationPreference? preference = await context.NotificationPreferences
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == userId && p.Category == category, cancellationToken);

        return preference?.Enabled ?? true;
    }

    private async Task CreateNotificationAsync(Guid userId, string text, string type)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Text = text,
            Type = type,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.Notifications.Add(notification);
        await Task.CompletedTask;
    }

    private async Task SendPushAsync(
        Guid userId,
        string title,
        string body,
        string relativePath,
        Uri? appBaseUri,
        CancellationToken cancellationToken)
    {
        string deepLink = appBaseUri is null
            ? relativePath
            : new Uri(appBaseUri, relativePath).ToString();

        List<PushSubscription> subs = await context.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (PushSubscription sub in subs)
        {
            try
            {
                await webPushService.SendPushNotificationAsync(sub, title, body, deepLink, cancellationToken);
            }
            catch
            {
                // Ignore individual push failures
            }
        }
    }
}
