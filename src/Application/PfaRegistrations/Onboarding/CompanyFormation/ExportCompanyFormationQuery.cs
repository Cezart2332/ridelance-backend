using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.Dossiers;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Documents;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

public sealed record CompanyFormationExport(string FileName, byte[] Content);

/// <summary>Pachetul trimis la Consulto (spec §9): fișa, semnătura, dovada și documentele.</summary>
public sealed record ExportCompanyFormationQuery(Guid PfaRegistrationId) : IQuery<CompanyFormationExport>;

internal sealed class ExportCompanyFormationQueryHandler(
    IApplicationDbContext context,
    IFileEncryptionService fileEncryptionService,
    ICompanyFormationPdfGenerator pdfGenerator)
    : IQueryHandler<ExportCompanyFormationQuery, CompanyFormationExport>
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>Documentele din Pasul 0 care însoțesc dosarul.</summary>
    private static readonly DocumentCategory[] AttachedCategories =
    [
        DocumentCategory.CarteIdentitate,
        DocumentCategory.Buletin,
        DocumentCategory.PermisConducere,
        DocumentCategory.AtestatTransport,
    ];

    public async Task<Result<CompanyFormationExport>> Handle(
        ExportCompanyFormationQuery query,
        CancellationToken cancellationToken)
    {
        CompanyFormationRequest? request = await context.CompanyFormationRequests
            .AsNoTracking()
            .Include(r => r.Owners)
            .Include(r => r.Consents)
            .Include(r => r.Signature)
            .Include(r => r.ConsultoOffice)
            .Include(r => r.PfaRegistration)
            .FirstOrDefaultAsync(r => r.PfaRegistrationId == query.PfaRegistrationId, cancellationToken);

        if (request is null)
        {
            return Result.Failure<CompanyFormationExport>(CompanyFormationErrors.NoRegistration);
        }

        if (request.Signature is null)
        {
            return Result.Failure<CompanyFormationExport>(CompanyFormationErrors.NotSubmitted);
        }

        string applicant = $"{request.Solicitant.Nume} {request.Solicitant.Prenume}".Trim();

        // Folderul poartă un hash al CNP-ului, nu CNP-ul: pachetul circulă prin e-mail.
        string folder = CnpHash(request.Solicitant);

        List<Document> attachments = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == request.PfaRegistration.UserId
                && AttachedCategories.Contains(d.Category)
                && d.Status != DocumentStatus.Rejected)
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        Document? signatureImage = request.Signature.ImageDocumentId is Guid imageId
            ? await context.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == imageId, cancellationToken)
            : null;

        DateTime generatedAtUtc = DateTime.UtcNow;

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteAsync(
                archive,
                $"{folder}/date-solicitant.pdf",
                pdfGenerator.GenerateApplicantSheet(BuildSheet(request, applicant, generatedAtUtc)),
                cancellationToken);

            await WriteAsync(
                archive,
                $"{folder}/dovada-consimtamant.pdf",
                pdfGenerator.GenerateConsentProof(BuildProof(request, applicant, generatedAtUtc)),
                cancellationToken);

            if (signatureImage is not null)
            {
                byte[]? content = await ReadDocumentAsync(signatureImage, cancellationToken);
                if (content is not null)
                {
                    await WriteAsync(archive, $"{folder}/semnatura.png", content, cancellationToken);
                }
            }

            // Câte un fișier per categorie — cea mai recentă versiune încărcată.
            foreach (DocumentCategory category in AttachedCategories)
            {
                Document? document = attachments.Find(d => d.Category == category);
                if (document is null)
                {
                    continue;
                }

                byte[]? content = await ReadDocumentAsync(document, cancellationToken);
                if (content is null)
                {
                    continue;
                }

                string extension = Path.GetExtension(document.OriginalFileName);
                string name = string.IsNullOrWhiteSpace(extension) ? category.ToString() : category + extension;
                await WriteAsync(archive, $"{folder}/{name}", content, cancellationToken);
            }

            await WriteAsync(
                archive,
                $"{folder}/metadata.json",
                JsonSerializer.SerializeToUtf8Bytes(BuildMetadata(request, applicant, generatedAtUtc)),
                cancellationToken);
        }

        return Result.Success(new CompanyFormationExport(
            $"infiintare-{folder}.zip",
            buffer.ToArray()));
    }

    private async Task<byte[]?> ReadDocumentAsync(Document document, CancellationToken cancellationToken)
    {
        try
        {
            using Stream decrypted = await fileEncryptionService.DecryptAndReadAsync(
                document.EncryptedFilePath, document.EncryptionIv, cancellationToken);

            using var memory = new MemoryStream();
            await decrypted.CopyToAsync(memory, cancellationToken);
            return memory.ToArray();
        }
        catch (IOException)
        {
            // Un fișier lipsă din storage nu are voie să blocheze tot pachetul.
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static async Task WriteAsync(
        ZipArchive archive,
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using Stream stream = await entry.OpenAsync(cancellationToken);
        await stream.WriteAsync(content, cancellationToken);
    }

    private static CompanyFormationSheetData BuildSheet(
        CompanyFormationRequest request,
        string applicant,
        DateTime generatedAtUtc)
    {
        var people = new List<CompanyFormationPerson>
        {
            new("Solicitant", PersonFields(request.Solicitant)),
        };

        foreach (CompanyFormationOwner owner in request.Owners.OrderBy(o => o.Position))
        {
            people.Add(new CompanyFormationPerson(
                $"Proprietar {owner.Position + 1}",
                PersonFields(owner.Persoana)));
        }

        var office = new List<CompanyFormationField>
        {
            new("Tip sediu", request.OfficeType switch
            {
                RegisteredOfficeType.ConsultoProvided => "Adresă pusă la dispoziție de Consulto",
                RegisteredOfficeType.Own => "Adresă proprie",
                _ => "—",
            }),
        };

        if (request.OfficeType == RegisteredOfficeType.ConsultoProvided)
        {
            office.Add(new CompanyFormationField("Adresă", request.ConsultoOffice?.ToDisplayString() ?? "—"));
        }
        else
        {
            office.Add(new CompanyFormationField("Adresă", FormatAddress(request.OfficeAddress)));
            office.Add(new CompanyFormationField(
                "Solicitantul e proprietar",
                request.IsOwner == true ? "Da" : "Nu"));
        }

        office.Add(new CompanyFormationField(
            "Confirmări",
            string.Join(
                " · ",
                new[]
                {
                    request.AcknowledgedOwnershipDocs ? "deține actele de proprietate" : null,
                    request.AcknowledgedSubmitLater ? "transmite actele ulterior" : null,
                    request.AcknowledgedOwnerConsent == true ? "acord scris al proprietarului" : null,
                }.Where(v => v is not null))));

        return new CompanyFormationSheetData(applicant, people, office, generatedAtUtc);
    }

    /// <summary>CNP-ul apare mascat: pachetul pleacă din sistem, valoarea în clar nu.</summary>
    private static List<CompanyFormationField> PersonFields(PersoanaFizica p) =>
    [
        new("Nume", p.Nume ?? "—"),
        new("Prenume", p.Prenume ?? "—"),
        new("CNP", p.CnpMasked ?? "—"),
        new("Act de identitate", $"{p.TipAct} {p.SerieAct} {p.NumarAct}".Trim()),
        new("Autoritate emitentă", p.AutoritateEmitenta ?? "—"),
        new("Data emiterii", p.DataEmiterii?.ToString("dd.MM.yyyy", Ro) ?? "—"),
        new("Data expirării", p.DataExpirarii?.ToString("dd.MM.yyyy", Ro) ?? "—"),
        new("Domiciliu", FormatAddress(p.Domiciliu)),
    ];

    private static CompanyFormationConsentProofData BuildProof(
        CompanyFormationRequest request,
        string applicant,
        DateTime generatedAtUtc)
    {
        CompanyFormationSignature signature = request.Signature!;

        return new CompanyFormationConsentProofData(
            applicant,
            request.Consents
                .OrderBy(c => c.AcceptedAtUtc)
                .Select(c => new CompanyFormationConsentLine(
                    c.StepKey, c.TextSnapshot, c.CheckboxLabelSnapshot, c.Version, c.AcceptedAtUtc))
                .ToList(),
            signature.IpAddress,
            signature.UserAgent,
            signature.DeviceType,
            signature.Os,
            signature.Browser,
            signature.SignedAtUtc,
            signature.PayloadHash,
            generatedAtUtc);
    }

    private static object BuildMetadata(
        CompanyFormationRequest request,
        string applicant,
        DateTime generatedAtUtc) =>
        new
        {
            requestId = request.Id,
            pfaRegistrationId = request.PfaRegistrationId,
            applicant,
            status = request.Status.ToString(),
            submittedAtUtc = request.SubmittedAtUtc,
            signedAtUtc = request.Signature?.SignedAtUtc,
            payloadHash = request.Signature?.PayloadHash,
            consentVersion = request.Consents.FirstOrDefault()?.Version,
            ownerCount = request.Owners.Count,
            officeType = request.OfficeType?.ToString(),
            generatedAtUtc,
        };

    private static string FormatAddress(Adresa a)
    {
        string[] parts =
        [
            string.IsNullOrWhiteSpace(a.Strada) ? string.Empty : $"Str. {a.Strada}",
            string.IsNullOrWhiteSpace(a.Numar) ? string.Empty : $"nr. {a.Numar}",
            string.IsNullOrWhiteSpace(a.Bloc) ? string.Empty : $"bl. {a.Bloc}",
            string.IsNullOrWhiteSpace(a.Scara) ? string.Empty : $"sc. {a.Scara}",
            string.IsNullOrWhiteSpace(a.Etaj) ? string.Empty : $"et. {a.Etaj}",
            string.IsNullOrWhiteSpace(a.Apartament) ? string.Empty : $"ap. {a.Apartament}",
            a.Localitate ?? string.Empty,
            string.IsNullOrWhiteSpace(a.Judet) ? string.Empty : $"jud. {a.Judet}",
        ];

        string joined = string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.IsNullOrEmpty(joined) ? "—" : joined;
    }

    /// <summary>
    /// Numele folderului: hash-ul valorii criptate a CNP-ului, scurtat. Identifică dosarul fără
    /// să expună nimic — Consulto oricum primește CNP-ul pe alt canal, dacă e cazul.
    /// </summary>
    private static string CnpHash(PersoanaFizica solicitant)
    {
        string source = solicitant.CnpEncrypted ?? solicitant.CnpMasked ?? "necunoscut";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
    }
}
