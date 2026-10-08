using System.Text.Json;
using Application.Abstractions;
using Application.Abstractions.Ai;
using Application.Documents.ExtractedFields;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Application.Abstractions.Security;
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
    IExtractedFieldApplier fieldApplier,
    ISecretProtector secretProtector,
    IDocumentForensics documentForensics,
    DocumentIdentityService identityService,
    IConfiguration configuration)
    : ICommandHandler<RunDocumentAiVerificationCommand>
{
    private const int MaxAttempts = 3;

    /// <summary>Prag sub care câmpul intră în verificarea manuală a adminului (nu blochează fluxul).</summary>
    private const double ManualReviewThreshold = 0.75;

    private const string BlankTemplateReason = "Formularul e necompletat.";

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
                fieldRequests,
                expectation.AuthenticityHints),
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
        // Se reține chiar dacă documentul pică verificarea: dosarul se generează și din acte pe
        // care noi le-am respins, iar orientarea rămâne corectă indiferent de verdict.
        document.AiRotationDegrees = result.RotationDegrees;
        // O verificare nouă pornește de la zero: motivele vechi erau ale citirii de atunci.
        document.AiSuspicionReasons = null;
        document.AiIdentityMismatch = false;

        ExtractedValues values = await PopulateExtractedFieldsAsync(document, expectation, result, cancellationToken);
        var reviewReasons = new List<string>();
        DateOnly today = DocumentDateValidator.TodayInRomania();

        DateOnly? expiresAt = ExpiryOf(expectation, result, values, reviewReasons);
        if (expiresAt.HasValue)
        {
            var expiresUtc = DateTime.SpecifyKind(expiresAt.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            document.AiExtractedExpiresAtUtc = expiresUtc;
            document.ExpiresAtUtc ??= expiresUtc;
        }

        AddFieldRuleReasons(document.Category, values, reviewReasons);

        // Autenticitatea: ce a văzut modelul (antet, ștampilă, „pare făcut acasă”) plus ce spune
        // fișierul despre el (PDF exportat din Word). Suspectul merge la admin; doar formularul
        // gol se respinge, ca un act lipsă.
        AuthenticityVerdict authenticity = DocumentAuthenticityEvaluator.Evaluate(
            expectation,
            result.Authenticity,
            documentForensics.Inspect(fileBytes, document.ContentType));
        reviewReasons.AddRange(authenticity.Reasons);

        if (DocumentIdentityService.IsIdentityDocument(document.Category))
        {
            reviewReasons.AddRange(MrzReasons(values, today));
        }

        // Documentul e al aceluiași om ca buletinul? Dacă nu, ce s-a citit din el nu se aplică
        // pe profil: eligibilitatea nu se calculează din permisul altcuiva.
        IReadOnlyList<string> identityMismatches = await identityService.MismatchesAsync(
            document, values.Plain, expectation.Label, cancellationToken);

        if (identityMismatches.Count > 0)
        {
            DocumentIdentityService.MarkMismatch(document, identityMismatches);
        }
        else
        {
            await values.ApplyPendingAsync(fieldApplier, document, cancellationToken);
        }

        if (reviewReasons.Count > 0)
        {
            document.AiRequiresManualReview = true;
            document.AiSuspicionReasons = ReviewReasons.Merge(document.AiSuspicionReasons, reviewReasons);
        }

        // Verificarea temporală se face aici, pe ceasul serverului. Modelul doar citește datele:
        // nu are ceas, iar când îl lăsam să judece respingea acte bune ca „eliberate în viitor".
        DocumentDateVerdict dates = DocumentDateValidator.Evaluate(
            result.IssuedOn,
            expiresAt,
            expectation.ExpectsExpiryDate,
            expectation.IssueDateOnly,
            today,
            expectation.ValidMonthsFromIssue);

        if (dates.NeedsManualReview)
        {
            document.AiRequiresManualReview = true;
        }

        bool isValid = result.MatchesExpectedType && result.IsReadable && !dates.IsRejected && !authenticity.IsBlankTemplate;

        if (isValid)
        {
            document.AiStatus = DocumentAiStatus.Passed;
            await context.SaveChangesAsync(cancellationToken);
            await AfterPassAsync(document, cancellationToken);
            return Result.Success();
        }

        document.AiStatus = DocumentAiStatus.Failed;

        // Motivul temporal e mai precis decât explicația modelului, care nu mai judecă asta.
        if (dates.IsRejected)
        {
            document.AiSummary = Truncate(dates.Reason, 1024);
        }
        else if (authenticity.IsBlankTemplate)
        {
            document.AiSummary = BlankTemplateReason;
        }

        /*
         * Comutatorul de testare (`Onboarding:AutoApproveDocuments`).
         *
         * Verificarea a rulat întreagă până aici — tip, lizibilitate, date, câmpuri extrase — și
         * tot ce a citit rămâne scris. Doar verdictul nu mai are consecințe: documentul nu se
         * respinge și nu rămâne `Failed`, fiindcă ecranele de înrolare tratează `Failed` ca pe un
         * document lipsă și pasul s-ar bloca acolo, nu unde vrem noi să vedem că se blochează.
         *
         * Motivul real nu se pierde: intră în rezumat, cu prefix, iar dosarul ajunge oricum sub
         * ochii adminului prin `AiRequiresManualReview`.
         */
        if (bool.TryParse(configuration["Onboarding:AutoApproveDocuments"], out bool auto) && auto)
        {
            document.AiStatus = DocumentAiStatus.Passed;
            document.AiRequiresManualReview = true;
            document.AiSummary = Truncate(
                $"[Mod de testare — verdictul nu blochează] {document.AiSummary}",
                1024);

            await context.SaveChangesAsync(cancellationToken);
            await AfterPassAsync(document, cancellationToken);
            return Result.Success();
        }

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

        string modelReason = string.IsNullOrWhiteSpace(result.Reason)
            ? "Documentul nu a trecut verificarea automată."
            : result.Reason.Trim();

        string reason = modelReason;
        if (dates.IsRejected)
        {
            reason = dates.Reason;
        }
        else if (authenticity.IsBlankTemplate)
        {
            reason = BlankTemplateReason;
        }
        string text =
            $"Documentul „{expectation.Label}” a fost respins la verificarea automată: {reason} Încarcă o variantă corectă.";

        context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = document.UserId,
            Text = text,
            Type = NotificationTypes.DocumentAiCheck,
                SectionKey = document.Category.ToString(),
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        await NotifyUserAsync(document.User, expectation.Label, reason, cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Expirarea documentului. Pe talon nu e „data de expirare” a modelului — acolo alegea orice dată
    /// îi ieșea în cale, de obicei a înmatriculării —, ci cea mai îndepărtată viză din rubrica ITP.
    /// </summary>
    private static DateOnly? ExpiryOf(
        DocumentAiExpectation expectation,
        DocumentAiAnalysisResult result,
        ExtractedValues values,
        List<string> reviewReasons)
    {
        if (expectation.ExpiryFromField is null)
        {
            return result.ExpiresAt;
        }

        DateOnly? itp = DocumentFieldRules.LatestItpDate(
            values.Get(expectation.ExpiryFromField),
            DocumentDateValidator.Parse(values.Get("data_prima_inmatriculare")),
            DocumentDateValidator.Parse(values.Get("data_inmatriculare")));

        if (itp is null)
        {
            reviewReasons.Add("Nu am găsit data ITP pe talon.");
        }

        return itp;
    }

    /// <summary>Regulile pe câmpuri care nu țin de format: permisul și concluzia adeverințelor.</summary>
    private static void AddFieldRuleReasons(DocumentCategory category, ExtractedValues values, List<string> reviewReasons)
    {
        if (category == DocumentCategory.PermisConducere &&
            DocumentFieldRules.CategoryBProblem(
                DocumentDateValidator.Parse(values.Get("category_b_obtained_on")),
                DocumentDateValidator.Parse(values.Get("permis_emis_la_4a"))) is string problem)
        {
            reviewReasons.Add(problem);
        }

        if (DocumentFieldRules.SaysUnfit(values.Get("concluzie")))
        {
            reviewReasons.Add("Concluzia de pe document este „inapt”.");
        }
    }

    /// <summary>
    /// Zona MRZ a buletinului: cifrele de control și potrivirea cu datele tipărite. Un act editat
    /// rar le nimerește. MRZ necitit nu spune nimic, deci nu adaugă nimic.
    /// </summary>
    private static IEnumerable<string> MrzReasons(ExtractedValues values, DateOnly today)
    {
        MrzReading? mrz = MrzValidator.Parse(values.Get("mrz_raw"), today);
        if (mrz is null)
        {
            return [];
        }

        if (!mrz.ChecksValid)
        {
            return ["Codul MRZ de pe buletin nu se verifică."];
        }

        return IdentityCrossCheck
            .Mismatches(IdentityFacts.From(mrz), IdentityFacts.From(values.Plain), "zona MRZ", compareDocumentNumbers: true)
            .Select(_ => "Datele tipărite pe buletin nu corespund cu zona MRZ.")
            .Distinct();
    }

    /// <summary>
    /// După un buletin trecut, refacem comparația pe celelalte acte ale omului; după orice act,
    /// eligibilitatea nu rămâne „Eligibil” cât timp unul pare al altcuiva.
    /// </summary>
    private async Task AfterPassAsync(Document document, CancellationToken cancellationToken)
    {
        if (DocumentIdentityService.IsIdentityDocument(document.Category) && !document.AiIdentityMismatch)
        {
            await identityService.RecheckOthersAsync(document, cancellationToken);
            return;
        }

        await identityService.HoldEligibilityAsync(document.UserId, cancellationToken);
    }

    /// <summary>
    /// Ce s-a citit din document, în clar, plus câmpurile de aplicat pe entitățile de business.
    /// Aplicarea se amână până după potrivirea cu buletinul: permisul altcuiva nu trebuie să ajungă
    /// pe profil nici măcar pentru o clipă.
    /// </summary>
    private sealed class ExtractedValues
    {
        private readonly List<(string Key, string Value)> pending = [];

        public Dictionary<string, string> Plain { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? Get(string key) => Plain.TryGetValue(key, out string? value) ? value : null;

        public void Add(string key, string value, bool applyToBusiness)
        {
            Plain[key] = value;
            if (applyToBusiness)
            {
                pending.Add((key, value));
            }
        }

        public async Task ApplyPendingAsync(IExtractedFieldApplier applier, Document document, CancellationToken cancellationToken)
        {
            // Userul nu confirmă nimic: precompletăm direct entitatea de business.
            // Adminul verifică/corectează ulterior din panoul dosarului.
            foreach ((string key, string value) in pending)
            {
                await applier.ApplyAsync(document, key, value, cancellationToken);
            }
        }
    }

    private async Task<ExtractedValues> PopulateExtractedFieldsAsync(
        Document document,
        DocumentAiExpectation expectation,
        DocumentAiAnalysisResult result,
        CancellationToken cancellationToken)
    {
        document.AiConfidence = result.OverallConfidence;
        var values = new ExtractedValues();

        if (expectation.FieldSpecs.Count == 0)
        {
            return values;
        }

        List<ExtractedField> existing = await context.ExtractedFields
            .Where(f => f.DocumentId == document.Id)
            .ToListAsync(cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;
        bool requiresManualReview = false;
        // §1 — datele de identitate citite nesigur nu blochează pe nimeni, dar dosarul trebuie
        // să ajungă sub ochii unui om. Se marchează la finalul buclei, o singură dată.
        bool identityUnreliable = false;
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

                if (IdentityConfidence.IsIdentityField(spec.Key))
                {
                    identityUnreliable = true;
                }

                continue;
            }

            double aiConfidence = field?.Confidence ?? 0d;
            bool validatorPassed = ExtractedFieldValidators.Validate(spec.Type, normalized);
            double effective = ExtractedFieldValidators.EffectiveConfidence(validatorPassed, aiConfidence);

            // Câmpurile de control (MRZ) nu trimit documentul la verificare pe încrederea modelului:
            // ele se verifică determinist, prin cifrele de control, iar o nepotrivire devine motiv.
            bool needsReview = !DocumentAiCatalog.IsControlField(spec.Key)
                && (!validatorPassed || effective < ManualReviewThreshold);
            if (needsReview)
            {
                requiresManualReview = true;
            }

            if (IdentityConfidence.IsIdentityField(spec.Key) && !IdentityConfidence.IsTrustworthy(effective))
            {
                identityUnreliable = true;
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
                    // Pentru câmpurile sensibile nici măcar valoarea brută nu se păstrează în clar.
                    AiValue = spec.Sensitive
                        ? SensitiveFieldProtection.Mask(spec.Type, normalized)
                        : field?.Value?.Trim(),
                    IsSensitive = spec.Sensitive,
                    CreatedAtUtc = nowUtc,
                };
                context.ExtractedFields.Add(row);
                existing.Add(row);
            }

            // AiValue rămâne imutabil (dovada primei extrageri); reîmprospătăm doar derivatele.
            row.AiNormalizedValue = SensitiveFieldProtection.StoreOcrValue(
                row,
                new ExtractedFieldSpecSensitivity(spec.Sensitive, spec.Type),
                normalized,
                secretProtector);
            row.AiConfidence = aiConfidence;
            row.ValidatorPassed = validatorPassed;
            row.EffectiveConfidence = effective;
            row.IsSensitive = spec.Sensitive;
            row.UpdatedAtUtc = nowUtc;

            // Valoarea confirmată de om câștigă întotdeauna — nu retrogradăm starea și nu
            // suprascriem coloana de business cu valoarea OCR.
            bool confirmedByHuman = row.ConfirmedSource != ExtractedFieldSource.None;
            if (!confirmedByHuman)
            {
                row.ReviewState = needsReview
                    ? ExtractedFieldReviewState.NeedsManualReview
                    : ExtractedFieldReviewState.Auto;
            }

            // Pentru comparații contează valoarea corectată de om, dacă există; la câmpurile
            // sensibile coloana confirmată ține doar masca, deci rămânem pe citirea în clar.
            string plain = confirmedByHuman && !spec.Sensitive ? row.ConfirmedValue ?? normalized : normalized;
            values.Add(spec.Key, plain, applyToBusiness: !confirmedByHuman);
        }

        document.AiExtractedJson = JsonSerializer.Serialize(redacted);
        document.AiRequiresManualReview = requiresManualReview;

        if (identityUnreliable)
        {
            await FlagManualIdentityReviewAsync(document, cancellationToken);
        }

        return values;
    }

    /// <summary>
    /// §1 — ridică steagul de verificare manuală pe dosarul PFA. Nu se coboară niciodată de
    /// aici: o a doua încărcare reușită nu șterge faptul că prima a fost citită prost, iar
    /// adminul decide când e rezolvat.
    /// </summary>
    private async Task FlagManualIdentityReviewAsync(Document document, CancellationToken cancellationToken)
    {
        Domain.PfaRegistrations.PfaRegistration? registration = await context.PfaRegistrations
            .Where(r => r.UserId == document.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is { } found)
        {
            found.RequiresManualIdentityReview = true;
        }
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
