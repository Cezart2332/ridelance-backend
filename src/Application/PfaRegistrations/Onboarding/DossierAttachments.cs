using Application.Abstractions.Data;
using Application.Abstractions.Dossiers;
using Application.Abstractions.Services;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>
/// Strânge documentele care satisfac cerințele unei secțiuni și le decriptează, ca dosarul generat
/// să conțină scanările propriu-zise, nu doar o listă de bifat.
/// </summary>
internal static class DossierAttachments
{
    /// <summary>
    /// Pentru fiecare cerință, cel mai recent document non-respins din categoriile acceptate.
    /// Ordinea rezultatului urmează ordinea cerințelor — așa iese dosarul cum îl vrea ARR-ul.
    /// Cerințele fără document încărcat sunt sărite, la fel ca înainte.
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
            .Where(d => d.UserId == userId
                && d.Status != DocumentStatus.Rejected
                && wanted.Contains(d.Category))
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

            attachments.Add(new DossierAttachment(requirement.Label, document.ContentType, content));
        }

        return attachments;
    }

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
