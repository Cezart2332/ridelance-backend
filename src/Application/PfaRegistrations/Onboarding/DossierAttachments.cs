using Application.Abstractions.Data;
using Application.Abstractions.Dossiers;
using Application.Abstractions.Services;
using Domain.Documents;
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
    /// statusul — asta o face <see cref="UnverifiedAsync"/> înainte, iar generarea se oprește acolo
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
    /// Actele care ar intra în dosar, dar n-au fost încă verificate de un om — după etichetă.
    /// Listă goală înseamnă că dosarul se poate genera.
    ///
    /// Dosarul se depune la ghișeu în numele clientului, deci nu se construiește din acte pe care
    /// nu le-a văzut nimeni: un act în așteptare sau respins îl blochează. Se uită la același act
    /// pe care l-ar alege <see cref="CollectAsync"/> — cel mai recent pe fiecare cerință — ca
    /// verificarea și generarea să nu poată ajunge la documente diferite.
    ///
    /// Cerințele fără niciun act încărcat nu blochează aici: rămân sărite, ca până acum.
    /// </summary>
    public static async Task<IReadOnlyList<string>> UnverifiedAsync(
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
            .AsNoTracking()
            .Where(d => d.UserId == userId && wanted.Contains(d.Category))
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        var unverified = new List<string>();
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
                unverified.Add(requirement.Label);
            }
        }

        return unverified;
    }

    /// <summary>
    /// Ce ține dosarul pe loc: actele cerute care lipsesc cu totul, apoi cele încă neverificate de
    /// un om. Listă goală înseamnă că dosarul se poate genera.
    ///
    /// `UnverifiedAsync` sărea peste cerințele fără act, deci un dosar cu o piesă lipsă trecea — și
    /// ieșea incomplet la ghișeu. Aceeași listă se trimite și aplicației, ca butonul de generare să
    /// spună dinainte ce așteaptă, nu abia după apăsare.
    /// </summary>
    public static async Task<IReadOnlyList<string>> PendingAsync(
        IApplicationDbContext context,
        Guid userId,
        IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements,
        CancellationToken cancellationToken)
    {
        DossierReadiness readiness = await ReadinessAsync(context, userId, requirements, cancellationToken);
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

        IReadOnlyList<string> unverified = await UnverifiedAsync(context, userId, requirements, cancellationToken);

        return new DossierReadiness(missing, unverified);
    }

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
