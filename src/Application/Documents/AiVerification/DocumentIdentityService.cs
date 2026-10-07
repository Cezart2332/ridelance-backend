using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Documents.ExtractedFields;
using Domain.Documents;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;

namespace Application.Documents.AiVerification;

/// <summary>
/// Leagă documentele unui șofer de buletinul lui.
///
/// Înainte nimic nu verifica asta: permisul, atestatul sau adeverința altcuiva treceau, iar
/// eligibilitatea se calcula din ele. Acum fiecare act personal se compară cu buletinul
/// (<see cref="IdentityCrossCheck"/>). La nepotrivire documentul merge la admin, iar ce s-a citit
/// din el nu ajunge pe profil până nu se lămurește.
/// </summary>
internal sealed class DocumentIdentityService(
    IApplicationDbContext context,
    ISecretProtector secretProtector,
    IExtractedFieldApplier fieldApplier)
{
    /// <summary>Actele din care se calculează eligibilitatea: o nepotrivire pe ele o oprește.</summary>
    private static readonly DocumentCategory[] EligibilityCategories =
    [
        DocumentCategory.PermisConducere,
        DocumentCategory.AtestatSofer,
        DocumentCategory.AtestatTransport,
        .. DocumentAiCatalog.IdentityCategories,
    ];

    private const string EligibilityHoldReason =
        "Un document nu pare să fie al titularului buletinului — îl verifică un om.";

    public static bool IsIdentityDocument(DocumentCategory category) =>
        DocumentAiCatalog.IdentityCategories.Contains(category);

    /// <summary>
    /// Nepotrivirile documentului față de buletin. Gol când se potrivește sau când încă nu există
    /// un buletin citit — atunci comparația se face mai târziu, la <see cref="RecheckOthersAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<string>> MismatchesAsync(
        Document document,
        IReadOnlyDictionary<string, string> fields,
        string documentLabel,
        CancellationToken cancellationToken)
    {
        var candidate = IdentityFacts.From(fields);
        if (candidate.IsEmpty)
        {
            return [];
        }

        bool isIdentity = IsIdentityDocument(document.Category);

        // Pentru un buletin, referința e celălalt act de identitate (poza cărții electronice față
        // de PDF-ul ei). Doar între categorii diferite: același tip încărcat din nou e un buletin
        // nou, cu altă serie, nu un fals.
        Document? reference = await LatestIdentityDocumentAsync(
            document.UserId,
            document.Id,
            isIdentity ? document.Category : null,
            cancellationToken);

        if (reference is null)
        {
            return [];
        }

        var referenceFacts = IdentityFacts.From(await FieldsOfAsync(reference.Id, cancellationToken));
        return IdentityCrossCheck.Mismatches(referenceFacts, candidate, documentLabel, compareDocumentNumbers: isIdentity);
    }

    /// <summary>
    /// După ce un buletin a trecut verificarea, refacem comparația pe celelalte acte personale ale
    /// omului: unele s-au putut încărca înaintea lui sau ale unui buletin vechi. Fără apel la model —
    /// doar pe câmpurile deja citite.
    /// </summary>
    public async Task RecheckOthersAsync(Document identityDocument, CancellationToken cancellationToken)
    {
        var reference = IdentityFacts.From(await FieldsOfAsync(identityDocument.Id, cancellationToken));
        if (reference.IsEmpty)
        {
            return;
        }

        List<Document> others = await context.Documents
            .Where(d => d.UserId == identityDocument.UserId &&
                        d.Id != identityDocument.Id &&
                        d.Status != DocumentStatus.Rejected &&
                        d.AiStatus == DocumentAiStatus.Passed &&
                        !DocumentAiCatalog.IdentityCategories.Contains(d.Category))
            .ToListAsync(cancellationToken);

        foreach (Document other in others)
        {
            var candidate = IdentityFacts.From(await FieldsOfAsync(other.Id, cancellationToken));
            if (candidate.IsEmpty)
            {
                continue;
            }

            IReadOnlyList<string> mismatches = IdentityCrossCheck.Mismatches(
                reference, candidate, DocumentAiCatalog.LabelFor(other.Category));

            if (mismatches.Count > 0)
            {
                MarkMismatch(other, mismatches);
            }
            else if (other.AiIdentityMismatch)
            {
                await ClearMismatchAsync(other, cancellationToken);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await HoldEligibilityAsync(identityDocument.UserId, cancellationToken);
    }

    /// <summary>Documentul pare al altcuiva: motivele intră la admin, câmpurile nu se aplică.</summary>
    public static void MarkMismatch(Document document, IReadOnlyList<string> mismatches)
    {
        document.AiIdentityMismatch = true;
        document.AiRequiresManualReview = true;
        document.AiSuspicionReasons = ReviewReasons.Merge(document.AiSuspicionReasons, mismatches);
    }

    /// <summary>
    /// Potrivirea a reușit (sau adminul a verificat actul): scoatem motivele de identitate și
    /// aplicăm acum ce s-a citit din document, cum s-ar fi întâmplat de la început.
    /// </summary>
    public async Task ClearMismatchAsync(Document document, CancellationToken cancellationToken)
    {
        document.AiIdentityMismatch = false;
        document.AiSuspicionReasons = ReviewReasons.Remove(document.AiSuspicionReasons, IdentityCrossCheck.IsIdentityReason);

        foreach ((string key, string value) in await FieldsOfAsync(document.Id, cancellationToken))
        {
            await fieldApplier.ApplyAsync(document, key, value, cancellationToken);
        }
    }

    /// <summary>
    /// Cât timp un act din care se calculează eligibilitatea pare al altcuiva, eligibilitatea nu
    /// poate fi „Eligibil”: rămâne la verificare, cu motivul scris. Adminul o validează oricum.
    /// </summary>
    public async Task HoldEligibilityAsync(Guid userId, CancellationToken cancellationToken)
    {
        OnboardingEligibilityProfile? profile = await context.OnboardingEligibilityProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile is null)
        {
            return;
        }

        await HoldIfMismatchedAsync(context, profile, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Coboară un „Eligibil” proaspăt calculat la „De verificat” dacă un act din care s-a calculat
    /// pare al altcuiva. Chemat oriunde se reevaluează eligibilitatea, ca regula să nu fie ocolită de
    /// o recalculare ulterioară. Nu salvează.
    /// </summary>
    public static async Task HoldIfMismatchedAsync(
        IApplicationDbContext context,
        OnboardingEligibilityProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile.Status != EligibilityStatus.Eligible)
        {
            return;
        }

        bool hasMismatch = await context.Documents.AnyAsync(
            d => d.UserId == profile.UserId &&
                 d.AiIdentityMismatch &&
                 d.Status != DocumentStatus.Verified &&
                 d.Status != DocumentStatus.Rejected &&
                 EligibilityCategories.Contains(d.Category),
            cancellationToken);

        if (!hasMismatch)
        {
            return;
        }

        profile.Status = EligibilityStatus.NeedsReview;
        profile.StatusReason = EligibilityHoldReason;
        profile.UpdatedAtUtc = DateTime.UtcNow;
    }

    private Task<Document?> LatestIdentityDocumentAsync(
        Guid userId,
        Guid excludeId,
        DocumentCategory? excludeCategory,
        CancellationToken cancellationToken) =>
        context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId &&
                        d.Id != excludeId &&
                        DocumentAiCatalog.IdentityCategories.Contains(d.Category) &&
                        (excludeCategory == null || d.Category != excludeCategory) &&
                        d.Status != DocumentStatus.Rejected &&
                        d.AiStatus == DocumentAiStatus.Passed &&
                        !d.AiIdentityMismatch)
            .OrderByDescending(d => d.UploadedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Câmpurile citite ale unui document, în clar (cele sensibile se decriptează).</summary>
    private async Task<Dictionary<string, string>> FieldsOfAsync(Guid documentId, CancellationToken cancellationToken)
    {
        List<ExtractedField> rows = await context.ExtractedFields
            .Where(f => f.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ExtractedField row in rows)
        {
            string? value = row.IsSensitive
                ? SensitiveFieldProtection.Reveal(row, secretProtector)
                : row.ConfirmedValue ?? row.AiNormalizedValue;

            if (!string.IsNullOrWhiteSpace(value))
            {
                fields[row.FieldKey] = value;
            }
        }

        return fields;
    }
}

/// <summary>Motivele de verificare manuală ale unui document, câte unul pe rând.</summary>
public static class ReviewReasons
{
    private const int MaxLength = 2048;

    public static IReadOnlyList<string> Split(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string? Merge(string? stored, IEnumerable<string> added) =>
        Join(Split(stored).Concat(added));

    public static string? Remove(string? stored, Func<string, bool> predicate) =>
        Join(Split(stored).Where(r => !predicate(r)));

    private static string? Join(IEnumerable<string> reasons)
    {
        string joined = string.Join('\n', reasons
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.Ordinal));

        if (joined.Length == 0)
        {
            return null;
        }

        return joined.Length <= MaxLength ? joined : joined[..MaxLength];
    }
}
