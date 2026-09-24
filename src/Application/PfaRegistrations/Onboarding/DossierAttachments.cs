using Application.Abstractions.Data;
using Application.Abstractions.Dossiers;
using Application.Abstractions.Services;
using Domain.Documents;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Strânge documentele care satisfac cerințele unei secțiuni și le decriptează, ca dosarul generat
/// să conțină scanările propriu-zise, nu doar o listă de bifat.
/// </summary>
internal static class DossierAttachments
{
    /// <summary>
    /// Pentru fiecare cerință, cel mai recent document din categoriile acceptate. Nu judecă
    /// statusul — asta o face <see cref="AwaitingValidationAsync"/> înainte, iar generarea se oprește acolo
    /// dacă vreun act nu e verificat de om.
    /// Ordinea rezultatului urmează ordinea cerințelor — așa iese dosarul cum îl vrea ARR-ul.
    /// Cerințele fără document încărcat sunt sărite.
    /// </summary>
    public static async Task<IReadOnlyList<DossierAttachment>> CollectAsync(
        IApplicationDbContext context,
        IFileEncryptionService fileEncryptionService,
        Guid userId,
        IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements,
        CancellationToken cancellationToken)
    {
        DocumentCategory[] wanted = requirements
            .SelectMany(req => req.AcceptedCategories)
            .Distinct()
            .ToArray();

        List<Document> documents = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId && wanted.Contains(d.Category))
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        var attachments = new List<DossierAttachment>(requirements.Count);
        var used = new HashSet<Guid>();

        foreach (OnboardingSectionCatalog.DocumentRequirement requirement in requirements)
        {
            // Lista e deja ordonată descrescător după dată, deci primul găsit e cel mai recent.
            // `used` împiedică același fișier să apară de două ori când două cerințe se suprapun.
            Document? document = documents.FirstOrDefault(d =>
                requirement.AcceptedCategories.Contains(d.Category) && !used.Contains(d.Id));

            if (document is null)
            {
                continue;
            }

            used.Add(document.Id);

            byte[]? content = await TryReadAsync(fileEncryptionService, document, cancellationToken);
            if (content is null)
            {
                continue;
            }

            attachments.Add(new DossierAttachment(
                requirement.Label, document.ContentType, content, document.AiRotationDegrees));
        }

        return attachments;
    }

    /// <summary>
    /// Refuzul de generare, cu actele care îl țin pe loc numite. Mesajul e pentru client: el vede
    /// ce anume așteaptă, nu doar că „nu se poate".
    /// </summary>
    public static Error NotYetVerified(IReadOnlyList<string> labels) => Error.Problem(
        "Onboarding.Dossier.DocumentsNotVerified",
        $"Dosarul se poate genera după ce echipa verifică toate actele. Încă în verificare: {string.Join(", ", labels)}.");

    /// <summary>
    /// Actele care ar intra în dosar și încă așteaptă validarea echipei — după etichetă.
    ///
    /// Validarea e o decizie pe dosar, nu statusul fiecărui act: `validatedAtUtc` e momentul în care
    /// adminul a apăsat „Validează documentele pentru dosar”. Până atunci, toate actele încărcate
    /// așteaptă. După, așteaptă doar ce s-a reîncărcat de atunci sau ce a fost respins între timp.
    ///
    /// Statusul actului nu ajungea: actele pot intra deja „verificate” (aprobarea automată din
    /// mediul de test, buletinul validat la pasul 1), iar dosarul se genera fără ca cineva să-l fi
    /// văzut. Se uită la același act pe care l-ar alege <see cref="CollectAsync"/> — cel mai recent
    /// pe fiecare cerință. Cerințele fără niciun act nu apar aici: sunt în lista celor lipsă.
    /// </summary>
    public static async Task<IReadOnlyList<string>> AwaitingValidationAsync(
        IApplicationDbContext context,
        Guid userId,
        IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements,
        DateTime? validatedAtUtc,
        CancellationToken cancellationToken)
    {
        DocumentCategory[] wanted = requirements
            .SelectMany(req => req.AcceptedCategories)
            .Distinct()
            .ToArray();

        List<Document> documents = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId && wanted.Contains(d.Category))
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        var awaiting = new List<string>();
        var used = new HashSet<Guid>();

        foreach (OnboardingSectionCatalog.DocumentRequirement requirement in requirements)
        {
            Document? document = documents.FirstOrDefault(d =>
                requirement.AcceptedCategories.Contains(d.Category) && !used.Contains(d.Id));

            if (document is null)
            {
                continue;
            }

            used.Add(document.Id);

            bool validated = validatedAtUtc is DateTime at
                && document.UploadedAtUtc <= at
                && document.Status != DocumentStatus.Rejected;
            if (!validated)
            {
                awaiting.Add(requirement.Label);
            }
        }

        return awaiting;
    }

    /// <summary>
    /// Ce ține dosarul pe loc: actele cerute care lipsesc cu totul, apoi cele care așteaptă
    /// validarea echipei. Listă goală înseamnă că dosarul se poate genera.
    /// </summary>
    public static async Task<IReadOnlyList<string>> PendingAsync(
        IApplicationDbContext context,
        Guid userId,
        IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements,
        DateTime? validatedAtUtc,
        CancellationToken cancellationToken)
    {
        DossierReadiness readiness = await ReadinessAsync(context, userId, requirements, validatedAtUtc, cancellationToken);
        return [.. readiness.Missing, .. readiness.Unverified];
    }

    /// <summary>
    /// Același răspuns ca <see cref="PendingAsync"/>, dar despărțit: ce lipsește (treaba clientului)
    /// și ce e încărcat dar încă nevalidat (treaba echipei, prin „Validează documentele pentru dosar”).
    /// </summary>
    public static async Task<DossierReadiness> ReadinessAsync(
        IApplicationDbContext context,
        Guid userId,
        IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements,
        DateTime? validatedAtUtc,
        CancellationToken cancellationToken)
    {
        DocumentCategory[] wanted = requirements
            .SelectMany(req => req.AcceptedCategories)
            .Distinct()
            .ToArray();

        List<DocumentCategory> present = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId && wanted.Contains(d.Category))
            .Select(d => d.Category)
            .Distinct()
            .ToListAsync(cancellationToken);

        var missing = requirements
            .Where(req => !req.AcceptedCategories.Any(present.Contains))
            .Select(req => req.Label)
            .ToList();

        IReadOnlyList<string> awaiting = await AwaitingValidationAsync(
            context, userId, requirements, validatedAtUtc, cancellationToken);

        return new DossierReadiness(missing, awaiting);
    }

    /// <summary>Momentul validării actelor pentru dosarul unui pas (<see cref="DossierSteps"/>).</summary>
    public static DateTime? ValidatedAt(PfaRegistration registration, string step) =>
        step == DossierSteps.Arr
            ? registration.ArrDossierDocumentsValidatedAtUtc
            : registration.VehicleDossierDocumentsValidatedAtUtc;

    /// <summary>
    /// Validarea din admin a actelor care intră în dosar, dintr-un singur clic: cel mai recent act
    /// pe fiecare cerință devine verificat. Același act pe care l-ar alege <see cref="CollectAsync"/>,
    /// deci generarea pornește exact din ce a validat echipa. Întoarce etichetele validate acum.
    /// </summary>
    public static async Task<IReadOnlyList<string>> VerifyLatestAsync(
        IApplicationDbContext context,
        Guid userId,
        IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements,
        CancellationToken cancellationToken)
    {
        DocumentCategory[] wanted = requirements
            .SelectMany(req => req.AcceptedCategories)
            .Distinct()
            .ToArray();

        List<Document> documents = await context.Documents
            .Where(d => d.UserId == userId && wanted.Contains(d.Category))
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        var verified = new List<string>();
        var used = new HashSet<Guid>();
        foreach (OnboardingSectionCatalog.DocumentRequirement requirement in requirements)
        {
            Document? document = documents.FirstOrDefault(d =>
                requirement.AcceptedCategories.Contains(d.Category) && !used.Contains(d.Id));
            if (document is null)
            {
                continue;
            }

            used.Add(document.Id);
            if (document.Status != DocumentStatus.Verified)
            {
                document.Status = DocumentStatus.Verified;
                document.ReviewNote = null;
                verified.Add(requirement.Label);
            }
        }

        return verified;
    }

    /// <summary>Refuzul de generare cu actele care lipsesc sau așteaptă verificarea.</summary>
    public static Error NotReady(IReadOnlyList<string> labels) => Error.Problem(
        "Onboarding.Dossier.DocumentsNotReady",
        $"Dosarul se poate genera după ce toate actele sunt încărcate și verificate de echipă. Încă așteptăm: {string.Join(", ", labels)}.");

    /// <summary>
    /// Un fișier care nu poate fi decriptat (șters de pe disc, cheie schimbată) nu are voie să
    /// pice generarea dosarului — rămâne doar nemenționat, iar restul dosarului se produce.
    /// </summary>
    private static async Task<byte[]?> TryReadAsync(
        IFileEncryptionService fileEncryptionService,
        Document document,
        CancellationToken cancellationToken)
    {
        try
        {
            using Stream stream = await fileEncryptionService.DecryptAndReadAsync(
                document.EncryptedFilePath, document.EncryptionIv, cancellationToken);

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>Ce ține dosarul pe loc: acte lipsă (le încarcă clientul) și acte nevalidate (le validează echipa).</summary>
public sealed record DossierReadiness(IReadOnlyList<string> Missing, IReadOnlyList<string> Unverified)
{
    public bool Ready => Missing.Count == 0 && Unverified.Count == 0;
}
