using Application.Abstractions.Data;
using Application.Abstractions.Dossiers;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Application.Accounting;
using Application.Accounting.Pfas;
using Application.Documents.AiVerification;
using Application.Documents.ExtractedFields;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Application.PfaRegistrations.Onboarding.AnafMandate;

/// <summary>Împuternicirea generată: documentul, numărul și ce lipsește din ea.</summary>
public sealed record AnafMandateResult(Guid DocumentId, string Number, IReadOnlyList<string> Missing);

/// <summary>
/// Generează împuternicirea ANAF cu datele clientului, la trimiterea pasului fiscal sau la cererea
/// adminului. Documentul e al nostru (<see cref="DocumentOrigin.SystemGenerated"/>): clientul nu-l
/// vede în onboarding, îl primește la semnat pe email, în pachetul de semnături.
///
/// Numărul se păstrează la regenerare: e același mandat, doar cu datele completate.
/// </summary>
internal sealed class AnafMandateService(
    IApplicationDbContext context,
    ISecretProtector secretProtector,
    IAnafMandatePdfGenerator pdfGenerator,
    IAnafMandateNumberGenerator numberGenerator,
    IFileEncryptionService fileEncryptionService,
    IOptions<AnafMandatarOptions> mandatarOptions)
{
    public async Task<AnafMandateResult?> GenerateAsync(Guid registrationId, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.User)
            .Include(r => r.CompanyFormationRequest)
            .Include(r => r.SignaturePacket)
                .ThenInclude(p => p!.Documents)
            .SingleOrDefaultAsync(r => r.Id == registrationId, cancellationToken);

        if (registration is null)
        {
            return null;
        }

        DateTime nowUtc = DateTime.UtcNow;
        OnboardingSignaturePacket packet = EnsurePacket(registration, nowUtc);

        AnafMandant mandant = await MandantAsync(registration, cancellationToken);
        AnafMandatarOptions o = mandatarOptions.Value;
        var mandatar = new AnafMandatar(o.FullName, o.Cnp, o.Domicile, o.IdSeries, o.IdNumber, o.Email);
        IReadOnlyList<string> missing = AnafMandateContent.Missing(mandant, mandatar);

        // Rândul din pachetul de semnături ține numărul mandatului și documentul de semnat.
        OnboardingSignatureDocument? slot = packet.Documents
            .FirstOrDefault(d => d.Type == SignatureDocumentType.PowerOfAttorneyAnaf);

        string number = string.IsNullOrWhiteSpace(slot?.Label)
            ? await numberGenerator.NextAsync(cancellationToken)
            : slot.Label;

        if (slot is null)
        {
            slot = new OnboardingSignatureDocument
            {
                Id = Guid.NewGuid(),
                PacketId = packet.Id,
                Type = SignatureDocumentType.PowerOfAttorneyAnaf,
            };
            context.OnboardingSignatureDocuments.Add(slot);
        }

        var data = new AnafMandateData(
            number,
            DateOnly.FromDateTime(AccountingPeriod.ToRomania(nowUtc)),
            mandant,
            mandatar);

        byte[] pdf = pdfGenerator.Generate(data);

        string storedFileName = $"{Guid.NewGuid()}.pdf";
        using var stream = new MemoryStream(pdf);
        EncryptedFileResult encrypted = await fileEncryptionService.EncryptAndSaveAsync(
            stream, storedFileName, cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = registration.UserId,
            PfaRegistrationId = registration.Id,
            OriginalFileName = $"Imputernicire_{number}.pdf",
            StoredFileName = storedFileName,
            ContentType = "application/pdf",
            Category = DocumentCategory.ImputernicireAnaf,
            Origin = DocumentOrigin.SystemGenerated,
            // Cu date lipsă rămâne „în așteptare”: adminul completează și o regenerează.
            Status = missing.Count == 0 ? DocumentStatus.Verified : DocumentStatus.Pending,
            ReviewNote = AnafMandateContent.Note(missing),
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = pdf.Length,
            UploadedAtUtc = nowUtc,
            IssuedAtUtc = nowUtc,
            AiStatus = DocumentAiStatus.None,
        };
        context.Documents.Add(document);

        // Varianta veche, nesemnată, dispare: altfel adminul ar avea două împuterniciri cu același număr.
        if (slot.DocumentId is Guid previousId)
        {
            Document? previous = await context.Documents.FirstOrDefaultAsync(
                d => d.Id == previousId && d.Category == DocumentCategory.ImputernicireAnaf,
                cancellationToken);

            if (previous is not null)
            {
                context.Documents.Remove(previous);
            }
        }

        slot.Label = number;
        slot.DocumentId = document.Id;
        slot.IsSigned = false;
        slot.SignedAtUtc = null;
        packet.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);

        return new AnafMandateResult(document.Id, number, missing);
    }

    private OnboardingSignaturePacket EnsurePacket(PfaRegistration registration, DateTime nowUtc)
    {
        if (registration.SignaturePacket is not null)
        {
            return registration.SignaturePacket;
        }

        var packet = new OnboardingSignaturePacket
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = nowUtc,
        };
        context.OnboardingSignaturePackets.Add(packet);
        registration.SignaturePacket = packet;
        return packet;
    }

    /// <summary>
    /// Titularul: întâi dosarul de înființare (pe „Nu am PFA” e completat și verificat de om), apoi,
    /// pentru ce lipsește, câmpurile citite din buletin. PFA-ul: din certificatul citit, cu sediul din
    /// dosarul de înființare dacă certificatul n-a dat unul.
    /// </summary>
    private async Task<AnafMandant> MandantAsync(PfaRegistration registration, CancellationToken cancellationToken)
    {
        CompanyFormationRequest? formation = registration.CompanyFormationRequest;

        Dictionary<string, string> identity = formation is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : AnafMandateContent.FieldsOf(
                formation.Solicitant,
                SensitiveFieldProtection.TryUnprotect(secretProtector, formation.Solicitant.CnpEncrypted));

        AnafMandateContent.FillGaps(identity, await IdentityDocumentFieldsAsync(registration.UserId, cancellationToken));

        string pfaName = PfaNames.Of(
            registration.LegalName,
            registration.HolderName,
            registration.FullName,
            registration.User.FirstName,
            registration.User.LastName);

        string? office = registration.ProfessionalOffice;
        if (string.IsNullOrWhiteSpace(office) && formation is not null)
        {
            office = AnafMandateContent.Address(formation.OfficeAddress);
        }

        return AnafMandateContent.Mandant(identity, pfaName, office, registration.Cui, registration.RegistryNumber);
    }

    /// <summary>
    /// Câmpurile citite din actele de identitate trecute, cel mai nou primul. Pe cartea electronică
    /// domiciliul stă doar în PDF-ul RO CEI Reader, iar seria și numărul pe poză, deci se combină.
    /// Un act marcat ca al altcuiva nu intră.
    /// </summary>
    private async Task<Dictionary<string, string>> IdentityDocumentFieldsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var candidates = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId &&
                        DocumentAiCatalog.IdentityCategories.Contains(d.Category) &&
                        d.Status != DocumentStatus.Rejected &&
                        d.AiStatus == DocumentAiStatus.Passed &&
                        !d.AiIdentityMismatch)
            .OrderByDescending(d => d.UploadedAtUtc)
            .Select(d => new { d.Id, d.Category })
            .ToListAsync(cancellationToken);

        // Doar cel mai nou act din fiecare categorie: un buletin vechi nu completează unul nou.
        var documentIds = candidates
            .GroupBy(d => d.Category)
            .Select(g => g.First().Id)
            .ToList();

        List<ExtractedField> rows = await context.ExtractedFields
            .AsNoTracking()
            .Where(f => documentIds.Contains(f.DocumentId))
            .ToListAsync(cancellationToken);

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Guid documentId in documentIds)
        {
            foreach (ExtractedField row in rows.Where(r => r.DocumentId == documentId))
            {
                string? value = row.IsSensitive
                    ? SensitiveFieldProtection.Reveal(row, secretProtector)
                    : row.ConfirmedValue ?? row.AiNormalizedValue;

                if (!string.IsNullOrWhiteSpace(value) && !fields.ContainsKey(row.FieldKey))
                {
                    fields[row.FieldKey] = value.Trim();
                }
            }
        }

        return fields;
    }
}
