using Application.Abstractions;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Application.Accounting.Ledger;
using Application.Documents.AiVerification;
using Application.Notifications;
using Domain.Accounting;
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
    DocumentIdentityService identityService,
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

        if (document.Category == DocumentCategory.Cheltuiala && document.PfaRegistrationId is {} pfaId)
        {
            if (user.Role == UserRole.Contabil && !await context.PfaRegistrations.AnyAsync(p => p.Id == pfaId && p.AssignedContabilId == user.Id, cancellationToken))
            {
                return Result.Failure(DocumentErrors.AccessDenied);
            }
            List<LedgerEntry> payments = await context.LedgerEntries.Where(e => e.PfaRegistrationId == pfaId && e.SourceDocumentId == document.Id).ToListAsync(cancellationToken);
            foreach (LedgerEntry payment in payments)
            {
                Result editable = await UpdateLedgerEntryCommandHandler.EnsureEditableAsync(context, payment, cancellationToken);
                if (editable.IsFailure)
                {
                    return editable;
                }
                if (command.Status == DocumentStatus.Rejected)
                {
                    payment.ReconciliationStatus = ReconciliationStatus.NeedsReview;
                    payment.Status = LedgerEntryStatus.NeedsReview;
                }
                else if (command.Status == DocumentStatus.Verified)
                {
                    ExpenseDocument? source = await context.ExpenseDocuments.SingleOrDefaultAsync(d => d.PfaRegistrationId == pfaId && d.DocumentId == document.Id, cancellationToken);
                    string? cui = await context.PfaRegistrations.Where(p => p.Id == pfaId).Select(p => p.Cui).SingleAsync(cancellationToken);
                    payment.ReconciliationStatus = ReceiptSplit.Confidence(source?.BeneficiaryCui, cui, payment.Category);
                    payment.Status = payment.ReconciliationStatus == ReconciliationStatus.Matched ? LedgerEntryStatus.Verified : LedgerEntryStatus.NeedsReview;
                }
            }
        }

        DocumentStatus previousStatus = document.Status;
        document.Status = command.Status;
        // Motivul ține doar cât documentul e respins: o aprobare ulterioară îl șterge, altfel ar
        // rămâne afișat lângă un document acceptat.
        if (command.Status != DocumentStatus.Rejected)
        {
            document.ReviewNote = null;
        }
        else if (!string.IsNullOrWhiteSpace(command.Note))
        {
            string note = command.Note.Trim();
            document.ReviewNote = note.Length > 1024 ? note[..1024] : note;
        }

        // Adminul a verificat actul pe care potrivirea cu buletinul îl oprise: ce s-a citit din el
        // ajunge acum pe profil, ca la orice document trecut.
        if (command.Status == DocumentStatus.Verified && document.AiIdentityMismatch)
        {
            await identityService.ClearMismatchAsync(document, cancellationToken);
        }

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
                Text = document.ReviewNote is null
                    ? $"Documentul „{documentLabel}” a fost respins de echipa RIDElance. Încarcă o variantă nouă."
                    : $"Documentul „{documentLabel}” a fost respins: {document.ReviewNote} Încarcă o variantă nouă.",
                Type = NotificationTypes.DocumentStatusUpdate,
                SectionKey = document.Category.ToString(),
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        if (notifyRejection)
        {
            await NotifyOwnerAsync(document.User, documentLabel, document.ReviewNote, cancellationToken);
        }

        return Result.Success();
    }

    private async Task NotifyOwnerAsync(User owner, string documentLabel, string? note, CancellationToken cancellationToken)
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
            note,
            "Încarcă documentul din nou",
            deepLinkUri);

        await emailService.SendEmailAsync(owner.Email, subject, mjmlRenderer.Render(mjml), cancellationToken);
    }
}
