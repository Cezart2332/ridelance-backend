using System.Text.Json;
using Application.Abstractions;
using Application.Abstractions.Ai;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Application.Abstractions.Services;
using Application.Notifications;
using Domain.Documents;
using Domain.Notifications;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Documents.AiVerification;

internal sealed class RunDocumentAiVerificationCommandHandler(
    IApplicationDbContext context,
    IDocumentAiAnalyzer analyzer,
    IFileEncryptionService fileEncryptionService,
    IWebPushService webPushService,
    IEmailService emailService,
    IMjmlRenderer mjmlRenderer,
    IConfiguration configuration)
    : ICommandHandler<RunDocumentAiVerificationCommand>
{
    private const int MaxAttempts = 3;

    /// <summary>Prag sub care câmpul intră în verificarea manuală a adminului (nu blochează fluxul).</summary>
    private const double ManualReviewThreshold = 0.75;

    public async Task<Result> Handle(
        RunDocumentAiVerificationCommand command,
        CancellationToken cancellationToken)
    {
        Document? document = await context.Documents
            .Include(d => d.User)
            .SingleOrDefaultAsync(d => d.Id == command.DocumentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure(DocumentErrors.NotFound(command.DocumentId));
        }

        DocumentAiExpectation? expectation = DocumentAiCatalog.For(document.Category);

        if (expectation is null)
        {
            document.AiStatus = DocumentAiStatus.None;
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        document.AiStatus = DocumentAiStatus.Processing;
        document.AiAttempts++;
        await context.SaveChangesAsync(cancellationToken);

        byte[] fileBytes;
        try
        {
            await using Stream decrypted = await fileEncryptionService.DecryptAndReadAsync(
                document.EncryptedFilePath,
                document.EncryptionIv,
                cancellationToken);
            using var memory = new MemoryStream();
            await decrypted.CopyToAsync(memory, cancellationToken);
            fileBytes = memory.ToArray();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            document.AiStatus = DocumentAiStatus.Error;
            document.AiSummary = "Fișierul nu a putut fi citit pentru verificarea automată.";
            document.AiProcessedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return Result.Failure(Error.Failure(
                "Documents.AiFileUnreadable",
                "Fișierul documentului nu a putut fi citit."));
        }

        var fieldRequests = expectation.FieldSpecs
            .Select(f => new AiFieldRequest(f.Key, f.Description, f.Type.ToString(), f.Required))
            .ToList();

        Result<DocumentAiAnalysisResult> analysis = await analyzer.AnalyzeAsync(
            new DocumentAiAnalysisRequest(
                fileBytes,
                document.ContentType,
                document.OriginalFileName,
                expectation.Label,
                expectation.Details,
                expectation.ExpectsExpiryDate,
                fieldRequests),
            cancellationToken);

        if (analysis.IsFailure)
        {
            document.AiStatus = document.AiAttempts >= MaxAttempts
                ? DocumentAiStatus.Error
                : DocumentAiStatus.Queued;
            document.AiProcessedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return Result.Failure(analysis.Error);
        }

        DocumentAiAnalysisResult result = analysis.Value;

        document.AiDetectedType = Truncate(result.DetectedType, 256);
        document.AiSummary = Truncate(result.Reason, 1024);
        document.AiProcessedAtUtc = DateTime.UtcNow;

        if (result.ExpiresAt.HasValue)
        {
            var expiresUtc = DateTime.SpecifyKind(
                result.ExpiresAt.Value.ToDateTime(TimeOnly.MinValue),
                DateTimeKind.Utc);
            document.AiExtractedExpiresAtUtc = expiresUtc;
            document.ExpiresAtUtc ??= expiresUtc;
        }

        await PopulateExtractedFieldsAsync(document, expectation, result, cancellationToken);

        bool isValid = result.IsValid &&
                       result.MatchesExpectedType &&
                       result.IsReadable &&
                       result.IsExpired != true;

        if (isValid)
        {
            document.AiStatus = DocumentAiStatus.Passed;
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        document.AiStatus = DocumentAiStatus.Failed;

        // Auto-respingerea e opțională per categorie: OCR-ul nu trebuie să blocheze fluxul.
        // Când e dezactivată, documentul rămâne în coada adminului (AiRequiresManualReview).
        if (!expectation.AutoRejectOnFailure)
        {
            document.AiRequiresManualReview = true;
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        if (document.Status == DocumentStatus.Pending)
        {
            document.Status = DocumentStatus.Rejected;
        }

        string reason = string.IsNullOrWhiteSpace(result.Reason)
            ? "Documentul nu a trecut verificarea automată."
            : result.Reason.Trim();
        string text =
            $"Documentul „{expectation.Label}” a fost respins la verificarea automată: {reason} Încarcă o variantă corectă.";

        context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = document.UserId,
            Text = text,
            Type = NotificationTypes.DocumentAiCheck,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        await NotifyUserAsync(document.User, expectation.Label, reason, cancellationToken);

        return Result.Success();
    }

    private async Task PopulateExtractedFieldsAsync(
        Document document,
        DocumentAiExpectation expectation,
        DocumentAiAnalysisResult result,
        CancellationToken cancellationToken)
    {
        document.AiConfidence = result.OverallConfidence;

        if (expectation.FieldSpecs.Count == 0)
        {
            return;
        }

        List<ExtractedField> existing = await context.ExtractedFields
            .Where(f => f.DocumentId == document.Id)
            .ToListAsync(cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;
        bool requiresManualReview = false;
        var redacted = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (ExtractedFieldSpec spec in expectation.FieldSpecs)
        {
            AiFieldResult? field = result.Fields
                .FirstOrDefault(f => string.Equals(f.Key, spec.Key, StringComparison.OrdinalIgnoreCase));

            string? normalized = ExtractedFieldValidators.Normalize(spec.Type, field?.Value);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                // Lipsă: contează doar dacă e obligatoriu (intră în coada adminului).
                if (spec.Required)
                {
                    requiresManualReview = true;
                }

                continue;
            }

            double aiConfidence = field?.Confidence ?? 0d;
            bool validatorPassed = ExtractedFieldValidators.Validate(spec.Type, normalized);
            double effective = ExtractedFieldValidators.EffectiveConfidence(validatorPassed, aiConfidence);

            bool needsReview = !validatorPassed || effective < ManualReviewThreshold;
            if (needsReview)
            {
                requiresManualReview = true;
            }

            redacted[spec.Key] = spec.Sensitive
                ? new { value = "***", confidence = effective }
                : new { value = normalized, confidence = effective };

            ExtractedField? row = existing.FirstOrDefault(f =>
                string.Equals(f.FieldKey, spec.Key, StringComparison.OrdinalIgnoreCase));

            if (row is null)
            {
                row = new ExtractedField
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    FieldKey = spec.Key,
                    AiValue = field?.Value?.Trim(),
                    IsSensitive = spec.Sensitive,
                    CreatedAtUtc = nowUtc,
                };
                context.ExtractedFields.Add(row);
                existing.Add(row);
            }

            // AiValue rămâne imutabil (dovada primei extrageri); reîmprospătăm doar derivatele.
            row.AiNormalizedValue = normalized;
            row.AiConfidence = aiConfidence;
            row.ValidatorPassed = validatorPassed;
            row.EffectiveConfidence = effective;
            row.IsSensitive = spec.Sensitive;
            row.UpdatedAtUtc = nowUtc;

            // Valoarea confirmată de om câștigă întotdeauna — nu retrogradăm starea.
            if (row.ConfirmedSource == ExtractedFieldSource.None)
            {
                row.ReviewState = needsReview
                    ? ExtractedFieldReviewState.NeedsManualReview
                    : ExtractedFieldReviewState.NeedsUserConfirmation;
            }
        }

        document.AiExtractedJson = JsonSerializer.Serialize(redacted);
        document.AiRequiresManualReview = requiresManualReview;
    }

    private async Task NotifyUserAsync(
        User user,
        string documentLabel,
        string reason,
        CancellationToken cancellationToken)
    {
        Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase)
            ? parsedBase
            : null;
        Uri deepLinkUri = appBaseUri is null
            ? new Uri("/onboarding", UriKind.Relative)
            : new Uri(appBaseUri, "/onboarding");
        string deepLink = deepLinkUri.ToString();

        List<PushSubscription> subscriptions = await context.PushSubscriptions
            .Where(s => s.UserId == user.Id)
            .ToListAsync(cancellationToken);

        foreach (PushSubscription sub in subscriptions)
        {
            try
            {
                await webPushService.SendPushNotificationAsync(
                    sub,
                    "Document respins",
                    $"„{documentLabel}” nu a trecut verificarea automată.",
                    deepLink,
                    cancellationToken);
            }
            catch
            {
                // Ignore push sending failures
            }
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        string subject = $"Document respins la verificarea automată — {documentLabel}";
        string mjml = EmailTemplates.Notice(
            "Document de reîncărcat",
            $"{user.FirstName} {user.LastName}".Trim(),
            [
                $"Documentul „{documentLabel}” încărcat în contul tău RIDElance a fost respins la verificarea automată.",
                "Un membru al echipei va verifica documentele tale în continuare, dar te rugăm să încarci o variantă corectă pentru a nu întârzia validarea.",
            ],
            reason,
            "Încarcă documentul din nou",
            deepLinkUri);

        await emailService.SendEmailAsync(user.Email, subject, mjmlRenderer.Render(mjml), cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
