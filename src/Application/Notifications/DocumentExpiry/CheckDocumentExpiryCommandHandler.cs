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
/// Anunță documentele care expiră: o notificare la fiecare prag (30, 14, 7 zile, ziua expirării)
/// în care documentul a intrat. Pragul nu mai trebuie nimerit la zi: un document încărcat cu 5
/// zile rămase sau o zi în care jobul n-a rulat primeau înainte zero notificări. Idempotent:
/// fiecare prag se anunță o singură dată pe document și dată de expirare.
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

    /// <summary>
    /// Când se anunță: cu o lună, cu două săptămâni, cu o săptămână înainte, și în ziua expirării.
    /// </summary>
    /// <remarks>
    /// Erau doar 30 și 7. Între ele treceau trei săptămâni fără niciun semn, iar în ziua expirării
    /// nu se spunea nimic — exact ziua în care mașina nu mai poate circula legal.
    /// </remarks>
    private static readonly int[] NotifyAtDaysBefore = [30, 14, 7, 0];

    /// <summary>Un document expirat de mai mult de atât nu mai primește „a expirat”: e deja știut.</summary>
    private const int ExpiredGraceDays = 7;

    /// <summary>Cel mai apropiat prag în care a intrat documentul; <c>null</c> = încă nu e cazul.</summary>
    internal static int? ThresholdFor(int daysUntilExpiry) =>
        daysUntilExpiry < -ExpiredGraceDays || daysUntilExpiry > NotifyAtDaysBefore.Max()
            ? null
            : NotifyAtDaysBefore.Where(t => t >= daysUntilExpiry).Min();

    public async Task<Result> Handle(
        CheckDocumentExpiryCommand command,
        CancellationToken cancellationToken)
    {
        TimeZoneInfo romania = Application.PfaRegistrations.MonthlyIncome.PfaMonthlyIncomeRecalculator.GetRomaniaTimeZone();
        DateTime nowRomania = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, romania);
        DateTime todayRomania = nowRomania.Date;

        // Fetch documents that have an expiry date and belong to expirable categories
        List<Document> documents = await context.Documents
            .AsNoTracking()
            .Where(d =>
                d.ExpiresAtUtc.HasValue &&
                !d.IsSuperseded &&
                ExpirableCategories.Contains(d.Category))
            .ToListAsync(cancellationToken);

        int notifsSent = 0;

        foreach (Document doc in documents)
        {
            DateTime expiryRomania = TimeZoneInfo
                .ConvertTimeFromUtc(doc.ExpiresAtUtc!.Value, romania)
                .Date;

            int daysUntilExpiry = (expiryRomania - todayRomania).Days;

            if (ThresholdFor(daysUntilExpiry) is not int threshold)
            {
                continue;
            }

            // Un prag pe document și dată de expirare: dacă data se corectează, pragurile reîncep.
            // Cheile vechi (cu ziua trimiterii la final) contează și ele, ca deployul să nu dubleze.
            string notifTag = $"expiry:{doc.Id}:{expiryRomania:yyyyMMdd}:{threshold}d";
            string legacyPrefix = $"expiry:{doc.Id}:{threshold}d:";
            bool alreadySent = await context.Notifications
                .AsNoTracking()
                .AnyAsync(n => n.DedupeKey == notifTag || n.DedupeKey!.StartsWith(legacyPrefix), cancellationToken);

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

            string ownerText = ExpiryText($"Documentul tău \"{categoryLabel}\"", daysUntilExpiry, expiryRomania);

            Guid? ownerNotificationId = null;
            Guid? accountantNotificationId = null;

            // Preferința titularului se respectă doar pentru el: contabila primește oricum
            // anunțul, pentru că e o obligație de serviciu, nu o notificare de confort.
            if (await OwnerWantsAsync(ownerId, NotificationTypes.DocumentExpiringSoon, cancellationToken))
            {
                ownerNotificationId = await CreateNotificationAsync(ownerId, ownerText, NotificationTypes.DocumentExpiringSoon, notifTag, ownerId, doc.Category.ToString());
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

                string contabilText = ExpiryText($"Clientul {ownerName}: \"{categoryLabel}\"", daysUntilExpiry, expiryRomania);

                accountantNotificationId = await CreateNotificationAsync(contabilId.Value, contabilText, NotificationTypes.DocumentExpiringSoon, $"{notifTag}:contabil", ownerId, doc.Category.ToString());
            }

            await context.SaveChangesAsync(cancellationToken);

            // Push notifications
            Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase) ? parsedBase : null;
            string pushTitle = daysUntilExpiry switch
            {
                < 0 => "Document expirat!",
                0 => "Document expiră azi!",
                1 => "Document expiră mâine",
                _ => $"Document expiră în {daysUntilExpiry} zile",
            };

            if (ownerNotificationId is not null)
            {
                await SendPushAsync(ownerId, pushTitle, categoryLabel, $"/app/notificari/{ownerNotificationId}", appBaseUri, cancellationToken);
            }
            if (contabilId.HasValue)
            {
                await SendPushAsync(contabilId.Value, pushTitle, categoryLabel, $"/app/notificari/{accountantNotificationId}", appBaseUri, cancellationToken);
            }

            notifsSent++;
            logger.LogInformation(
                "Expiry notification sent for document {DocId} ({Category}), {Days} days left.",
                doc.Id, doc.Category, daysUntilExpiry);
        }

        logger.LogInformation("Document expiry check complete. Notifications sent: {Count}", notifsSent);
        return Result.Success();
    }

    private static string ExpiryText(string subject, int days, DateTime expiry) => days switch
    {
        < 0 => $"{subject} a expirat pe {expiry:dd.MM.yyyy}.",
        0 => $"{subject} expiră astăzi!",
        1 => $"{subject} expiră mâine ({expiry:dd.MM.yyyy}).",
        _ => $"{subject} expiră în {days} zile ({expiry:dd.MM.yyyy}).",
    };

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

    private async Task<Guid> CreateNotificationAsync(Guid userId, string text, string type, string? dedupeKey = null, Guid? relatedUserId = null, string? sectionKey = null)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Text = text,
            Type = type,
            IsRead = false,
            DedupeKey = dedupeKey,
            RelatedUserId = relatedUserId,
            SectionKey = sectionKey,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.Notifications.Add(notification);
        await Task.CompletedTask;
        return notification.Id;
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
