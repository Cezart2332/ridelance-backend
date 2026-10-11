using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Application.Abstractions.Anaf;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.Months;
using Application.Accounting.Pfas;
using Application.PfaRegistrations.Onboarding.AnafMandate;
using Domain.Accounting;
using Domain.Documents;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.VatRegistration;

/// <summary>Ce s-a scris în XML la generare; rămâne ca să se vadă ce a semnat clientul.</summary>
/// <param name="Representative">Împuternicitul care depune cererea; lipsește la cererile vechi, depuse de titular.</param>
/// <param name="MandateDocumentId">Împuternicirea semnată, care se atașează declarației.</param>
internal sealed record D700Snapshot(
    string Cui,
    string Name,
    string DeclarantLastName,
    string DeclarantFirstName,
    string DeclarantFunction,
    D700Representative? Representative = null,
    Guid? MandateDocumentId = null);

/// <summary>Rezultatul validatorului ANAF, păstrat pe cerere.</summary>
internal sealed record D700Validation(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, DateTime ValidatedAt);

internal sealed record VatRegistrationHistory(VatRegistrationStatus? From, VatRegistrationStatus To, DateTime At, Guid? ByUserId, string? Note);

/// <summary>Fișierul cu certificatul ANAF, încărcat la „Cod primit”.</summary>
public sealed record VatCertificateFile(string FileName, string ContentType, byte[] Content);

public static class VatRegistrationErrors
{
    public static readonly Error NotFound = Error.NotFound("VatRegistration.NotFound", "Cererea D700 nu există.");

    public static readonly Error Locked = Error.Conflict(
        "VatRegistration.Locked",
        "Cererea D700 e deja aprobată sau depusă; nu se mai regenerează.");

    public static readonly Error ReasonRequired = Error.Problem("VatRegistration.ReasonRequired", "Motivul respingerii e obligatoriu.");

    public static readonly Error CertificateRequired = Error.Problem(
        "VatRegistration.CertificateRequired", "Codul se activează doar cu dovada ANAF din vectorul fiscal (certificatul de înregistrare în scopuri de TVA).");

    public static readonly Error MandateMissing = Error.Problem(
        "VatRegistration.MandateMissing",
        "Lipsește fișierul împuternicirii ANAF care se atașează cererii; regenerează cererea.");

    public static readonly Error NoValidator = Error.Problem(
        "VatRegistration.NoValidator",
        "Nu e configurată versiunea kitului ANAF (schemele declarațiilor).");

    public static Error WrongStatus(VatRegistrationStatus status) => Error.Conflict(
        "VatRegistration.WrongStatus",
        $"Acțiunea nu e posibilă când cererea e „{VatRegistrationLabels.Of(status)}”.");
}

internal static class VatRegistrationLabels
{
    public static string Of(VatRegistrationStatus status) => status switch
    {
        VatRegistrationStatus.WaitingForData => "lipsesc date",
        VatRegistrationStatus.Generated => "generată",
        VatRegistrationStatus.ValidationFailed => "validare picată",
        VatRegistrationStatus.ReadyForReview => "de verificat",
        VatRegistrationStatus.Approved => "aprobată",
        VatRegistrationStatus.Rejected => "respinsă",
        VatRegistrationStatus.Submitted => "depusă",
        VatRegistrationStatus.Registered => "cod primit",
        _ => status.ToString(),
    };
}

/// <summary>
/// Cererea D700 pentru codul de TVA art. 317: se generează din datele PFA-ului (niciun câmp
/// completat de mână), trece prin validatorul ANAF, apoi contabilul o aprobă sau o respinge. După
/// depunere, codul primit ajunge în setările contabile și în profilul fiscal.
/// </summary>
internal sealed class VatRegistrationService(
    IApplicationDbContext db,
    DeclarationFiles files,
    IVatRegistrationXml xml,
    IAnafValidatorClient anaf,
    ICommandHandler<UpdatePfaSettingsCommand, PfaAccountingSettingsDto> settings,
    IOptions<AnafMandatarOptions> mandatarOptions)
{
    /// <summary>Calitatea în care semnează omul RIDElance.</summary>
    public const string RepresentativeFunction = "IMPUTERNICIT";

    /// <summary>Stările în care cererea încă se poate regenera din datele actuale.</summary>
    private static readonly VatRegistrationStatus[] Open =
    [
        VatRegistrationStatus.WaitingForData,
        VatRegistrationStatus.Generated,
        VatRegistrationStatus.ValidationFailed,
        VatRegistrationStatus.ReadyForReview,
        VatRegistrationStatus.Rejected,
    ];

    /// <summary>
    /// Clientul a răspuns „Nu” în onboarding: cererea se creează o singură dată. Una existentă se
    /// regenerează doar dacă îi lipseau datele (de ex. CUI-ul a apărut între timp).
    /// </summary>
    public async Task<Result<VatRegistrationRequest>> EnsureRequestedAsync(Guid pfaId, Guid? userId, CancellationToken cancellationToken)
    {
        VatRegistrationRequest? latest = await LatestAsync(pfaId, cancellationToken);
        return latest is not null && latest.Status != VatRegistrationStatus.WaitingForData
            ? latest
            : await GenerateAsync(pfaId, userId, cancellationToken);
    }

    /// <summary>Generează (sau regenerează) XML-ul din datele actuale ale PFA-ului.</summary>
    public async Task<Result<VatRegistrationRequest>> GenerateAsync(Guid pfaId, Guid? userId, CancellationToken cancellationToken)
    {
        AnafTaxpayer? taxpayer = await files.TaxpayerAsync(pfaId, cancellationToken);
        if (taxpayer is null)
        {
            return Result.Failure<VatRegistrationRequest>(AccountingErrors.PfaNotFound);
        }

        VatRegistrationRequest? request = await LatestAsync(pfaId, cancellationToken);
        if (request is not null && !Open.Contains(request.Status))
        {
            return Result.Failure<VatRegistrationRequest>(VatRegistrationErrors.Locked);
        }

        DateTime now = DateTime.UtcNow;
        VatRegistrationStatus? from = request?.Status;
        if (request is null)
        {
            request = new VatRegistrationRequest
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = pfaId,
                CreatedByUserId = userId,
                CreatedAtUtc = now,
            };
            db.VatRegistrationRequests.Add(request);
        }

        request.Period = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        request.XmlDocumentId = null;
        request.PdfDocumentId = null;
        request.ValidationJson = null;
        request.RejectionReason = null;
        request.UpdatedAtUtc = now;

        // Cererea o depune împuternicitul RIDElance, în baza împuternicirii clientului. Dacă datele
        // împuternicitului sau împuternicirea lipsesc, cererea NU așteaptă după ele: se generează pe
        // titular, ca înainte, iar ce lipsește rămâne scris pe cerere. Fluxul clientului nu se
        // oprește pentru ceva ce ține de noi.
        (D700Representative? representative, Guid? mandateDocumentId, string? missingMandate) = await RepresentativeAsync(pfaId, cancellationToken);
        string? missing = Missing(taxpayer);
        if (missing is not null)
        {
            request.Status = VatRegistrationStatus.WaitingForData;
            request.MissingData = missing;
        }
        else
        {
            AnafMandatarOptions mandatar = mandatarOptions.Value;
            D700Snapshot snapshot = representative is null
                ? new D700Snapshot(taxpayer.Cui, taxpayer.Name, taxpayer.DeclarantLastName, taxpayer.DeclarantFirstName, taxpayer.DeclarantFunction)
                : new D700Snapshot(
                    taxpayer.Cui, taxpayer.Name, mandatar.LastName!.Trim(), mandatar.FirstName!.Trim(), RepresentativeFunction, representative, mandateDocumentId);
            byte[] content = xml.Build(new D700Content(
                request.Period, snapshot.Cui, snapshot.Name, snapshot.DeclarantLastName, snapshot.DeclarantFirstName, snapshot.DeclarantFunction, representative));
            Document document = await files.StoreAsync(pfaId, content, $"D700_{taxpayer.Cui}_{request.Period}.xml", "application/xml", cancellationToken);
            request.XmlDocumentId = document.Id;
            request.SnapshotJson = AccountingJson.Serialize(snapshot);
            request.MissingData = representative is null && missingMandate is not null
                ? $"Generată pe titular. Pentru depunerea prin împuternicit: {missingMandate}"
                : null;
            request.Status = VatRegistrationStatus.Generated;
        }

        Move(request, from, request.Status, userId, request.MissingData);
        AccountingAudit.Record(db, pfaId, nameof(VatRegistrationRequest), request.Id, "GENERATE", null, new { status = request.Status, request.Period }, request.MissingData, userId);
        await db.SaveChangesAsync(cancellationToken);
        return request;
    }

    /// <summary>Validatorul ANAF (DUKIntegrator) și PDF-ul de semnat.</summary>
    public async Task<Result<VatRegistrationRequest>> ValidateAsync(Guid id, Guid? userId, CancellationToken cancellationToken)
    {
        VatRegistrationRequest? request = await db.VatRegistrationRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return Result.Failure<VatRegistrationRequest>(VatRegistrationErrors.NotFound);
        }

        if (request.Status is not (VatRegistrationStatus.Generated or VatRegistrationStatus.ValidationFailed or VatRegistrationStatus.ReadyForReview))
        {
            return Result.Failure<VatRegistrationRequest>(VatRegistrationErrors.WrongStatus(request.Status));
        }

        string? kit = await db.AnafDeclarationSchemas.AsNoTracking()
            .Where(s => s.ValidatorVersion != null)
            .OrderByDescending(s => s.ValidFrom)
            .Select(s => s.ValidatorVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(kit))
        {
            return Result.Failure<VatRegistrationRequest>(VatRegistrationErrors.NoValidator);
        }

        (byte[] Content, Document Document)? stored = await files.ReadAsync(request.XmlDocumentId, cancellationToken);
        if (stored is null)
        {
            return Result.Failure<VatRegistrationRequest>(AccountingErrors.DocumentNotFound);
        }

        // O cerere depusă prin împuternicit are împuternicirea atașată; fără ea ANAF o respinge.
        D700Snapshot? generated = AccountingJson.Deserialize<D700Snapshot?>(request.SnapshotJson, null);
        byte[]? attachment = null;
        if (generated?.Representative is not null)
        {
            (byte[] Content, Document Document)? mandate = await files.ReadAsync(generated.MandateDocumentId, cancellationToken);
            if (mandate is null)
            {
                return Result.Failure<VatRegistrationRequest>(VatRegistrationErrors.MandateMissing);
            }

            attachment = Zip(mandate.Value.Document.OriginalFileName, mandate.Value.Content);
        }

        Result<AnafValidatorResult> called = attachment is null
            ? await anaf.ValidateAsync(IVatRegistrationXml.DeclarationType, kit, stored.Value.Content, request.Id.ToString(), cancellationToken)
            : await anaf.ValidateWithAttachmentAsync(IVatRegistrationXml.DeclarationType, kit, stored.Value.Content, attachment, request.Id.ToString(), cancellationToken);
        if (called.IsFailure)
        {
            return Result.Failure<VatRegistrationRequest>(called.Error);
        }

        AnafValidatorResult result = called.Value;
        VatRegistrationStatus from = request.Status;
        request.ValidationJson = AccountingJson.Serialize(new D700Validation(
            [.. result.Errors.Select(Text)],
            [.. result.Warnings.Select(Text)],
            DateTime.UtcNow));
        if (result.Valid && result.Pdf is { Length: > 0 } pdf)
        {
            D700Snapshot snapshot = AccountingJson.Deserialize<D700Snapshot?>(request.SnapshotJson, null)
                ?? new D700Snapshot(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
            Document document = await files.StoreAsync(request.PfaRegistrationId, pdf, $"D700_{snapshot.Cui}_{request.Period}.pdf", "application/pdf", cancellationToken);
            request.PdfDocumentId = document.Id;
            request.Status = VatRegistrationStatus.ReadyForReview;
        }
        else
        {
            request.PdfDocumentId = null;
            request.Status = VatRegistrationStatus.ValidationFailed;
        }

        request.UpdatedAtUtc = DateTime.UtcNow;
        Move(request, from, request.Status, userId, null);
        AccountingAudit.Record(db, request.PfaRegistrationId, nameof(VatRegistrationRequest), request.Id, "VALIDATE", new { status = from }, new { status = request.Status }, null, userId);
        await db.SaveChangesAsync(cancellationToken);
        return request;
    }

    /// <summary>Aprobare, respingere, depunere: tranziții fără fișiere.</summary>
    public async Task<Result<VatRegistrationRequest>> TransitionAsync(
        Guid id,
        VatRegistrationStatus to,
        string? note,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        VatRegistrationRequest? request = await db.VatRegistrationRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return Result.Failure<VatRegistrationRequest>(VatRegistrationErrors.NotFound);
        }

        VatRegistrationStatus[] allowedFrom = to switch
        {
            VatRegistrationStatus.Approved => [VatRegistrationStatus.ReadyForReview],
            VatRegistrationStatus.Rejected =>
            [
                VatRegistrationStatus.Generated,
                VatRegistrationStatus.ValidationFailed,
                VatRegistrationStatus.ReadyForReview,
                VatRegistrationStatus.Approved,
            ],
            VatRegistrationStatus.Submitted => [VatRegistrationStatus.Approved],
            _ => [],
        };
        if (!allowedFrom.Contains(request.Status))
        {
            return Result.Failure<VatRegistrationRequest>(VatRegistrationErrors.WrongStatus(request.Status));
        }

        string? reason = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (to == VatRegistrationStatus.Rejected)
        {
            if (reason is null)
            {
                return Result.Failure<VatRegistrationRequest>(VatRegistrationErrors.ReasonRequired);
            }

            request.RejectionReason = reason;
        }

        VatRegistrationStatus from = request.Status;
        request.Status = to;
        request.UpdatedAtUtc = DateTime.UtcNow;
        Move(request, from, to, userId, reason);
        AccountingAudit.Record(db, request.PfaRegistrationId, nameof(VatRegistrationRequest), request.Id, to.ToString().ToUpperInvariant(), new { status = from }, new { status = to }, reason, userId);
        await db.SaveChangesAsync(cancellationToken);
        return request;
    }

    /// <summary>
    /// ANAF a atribuit codul: se scrie în setările contabile (de la data certificatului, ca D301 și
    /// D390 să-l folosească) și în profilul fiscal; certificatul devine documentul codului.
    /// </summary>
    public async Task<Result<VatRegistrationRequest>> RegisterAsync(
        Guid id,
        string vatCode,
        DateOnly validFrom,
        VatCertificateFile? certificate,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        VatRegistrationRequest? request = await db.VatRegistrationRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return Result.Failure<VatRegistrationRequest>(VatRegistrationErrors.NotFound);
        }

        // F03: codul devine activ după depunere și după confirmarea ANAF din vectorul fiscal
        // (certificatul de înregistrare), nu la aprobare și nu doar la depunere.
        if (request.Status != VatRegistrationStatus.Submitted)
        {
            return Result.Failure<VatRegistrationRequest>(VatRegistrationErrors.WrongStatus(request.Status));
        }

        if (certificate is not { Content.Length: > 0 })
        {
            return Result.Failure<VatRegistrationRequest>(VatRegistrationErrors.CertificateRequired);
        }

        if (PfaSettings.Art317VatCode(vatCode) is not { } code)
        {
            return Result.Failure<VatRegistrationRequest>(PfaSettings.InvalidVatCode);
        }

        Result<PfaAccountingSettingsDto> saved = await settings.Handle(
            new UpdatePfaSettingsCommand(
                request.PfaRegistrationId,
                new SettingsChange(PfaAccountingSettingKeys.Art317VatCode, JsonSerializer.SerializeToElement(code), validFrom, "Cod obținut prin D700.")),
            cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<VatRegistrationRequest>(saved.Error);
        }

        Guid? certificateId = null;
        if (certificate is { Content.Length: > 0 })
        {
            Document document = await files.StoreAsync(
                request.PfaRegistrationId, certificate.Content, certificate.FileName, certificate.ContentType, cancellationToken, DocumentOrigin.SystemGenerated);
            document.Category = DocumentCategory.CertificatTvaIntracomunitar;
            certificateId = document.Id;
        }

        PfaFiscalProfile? profile = await db.PfaFiscalProfiles.SingleOrDefaultAsync(p => p.PfaRegistrationId == request.PfaRegistrationId, cancellationToken);
        if (profile is not null)
        {
            profile.VatRegistrationKind = VatRegistrationKind.SpecialArticle317;
            profile.SpecialVatCodeStatus = PfaSpecialVatCodeStatus.Yes;
            profile.SpecialVatCodeObtainedAtUtc = validFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            profile.SpecialVatCodeDocumentId = certificateId ?? profile.SpecialVatCodeDocumentId;
            profile.UpdatedAtUtc = DateTime.UtcNow;
            profile.UpdatedByUserId = userId;
        }

        VatRegistrationStatus from = request.Status;
        request.Status = VatRegistrationStatus.Registered;
        request.VatCode = code;
        request.VatCodeValidFrom = validFrom;
        request.CertificateDocumentId = certificateId;
        request.UpdatedAtUtc = DateTime.UtcNow;
        Move(request, from, request.Status, userId, code);
        AccountingAudit.Record(db, request.PfaRegistrationId, nameof(VatRegistrationRequest), request.Id, "REGISTER", new { status = from }, new { status = request.Status, vatCode = code, validFrom }, null, userId);
        await db.SaveChangesAsync(cancellationToken);
        return request;
    }

    private Task<VatRegistrationRequest?> LatestAsync(Guid pfaId, CancellationToken cancellationToken) =>
        db.VatRegistrationRequests
            .Where(r => r.PfaRegistrationId == pfaId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Împuternicitul și actul lui: datele omului RIDElance vin din configurarea serverului
    /// (<c>AnafMandatar</c>, aceleași ca pe împuternicire), iar numărul și data actului din
    /// împuternicirea generată la onboarding. Al treilea element spune ce lipsește.
    /// </summary>
    /// <remarks>
    /// Semnătura clientului pe împuternicire NU e o condiție aici: semnarea e alt pas al
    /// onboardingului, cu ritmul lui, iar cererea de cod TVA nu are de ce să aștepte după el. Se
    /// atașează documentul din pachet, în starea în care e.
    /// </remarks>
    private async Task<(D700Representative? Representative, Guid? MandateDocumentId, string? Missing)> RepresentativeAsync(
        Guid pfaId, CancellationToken cancellationToken)
    {
        AnafMandatarOptions mandatar = mandatarOptions.Value;
        List<string> missing = [];
        void Require(string? value, string what)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add(what);
            }
        }

        Require(mandatar.LastName, "numele împuternicitului (ANAF_MANDATAR_LAST_NAME)");
        Require(mandatar.FirstName, "prenumele împuternicitului (ANAF_MANDATAR_FIRST_NAME)");
        Require(mandatar.Cnp, "CNP-ul împuternicitului (ANAF_MANDATAR_CNP)");
        Require(mandatar.Domicile, "domiciliul împuternicitului (ANAF_MANDATAR_DOMICILE)");
        string? phone = string.IsNullOrWhiteSpace(mandatar.Phone) ? null : new string([.. mandatar.Phone.Where(char.IsDigit)]);
        if (phone is { Length: not 10 })
        {
            missing.Add("telefonul împuternicitului trebuie să aibă 10 cifre (ANAF_MANDATAR_PHONE)");
        }

        var mandate = await db.OnboardingSignatureDocuments.AsNoTracking()
            .Where(d => d.Packet.PfaRegistrationId == pfaId && d.Type == SignatureDocumentType.PowerOfAttorneyAnaf)
            .Select(d => new { d.Label, d.DocumentId })
            .FirstOrDefaultAsync(cancellationToken);
        DateTime? issuedAt = mandate?.DocumentId is { } documentId
            ? await db.Documents.AsNoTracking().Where(d => d.Id == documentId).Select(d => d.IssuedAtUtc ?? d.UploadedAtUtc).FirstOrDefaultAsync(cancellationToken)
            : null;
        if (mandate is null || string.IsNullOrWhiteSpace(mandate.Label) || mandate.DocumentId is null || issuedAt is null)
        {
            missing.Add("împuternicirea ANAF a clientului (nu a fost generată)");
        }

        if (missing.Count > 0)
        {
            return (null, null, $"lipsesc {string.Join("; ", missing)}.");
        }

        var representative = new D700Representative(
            ActNumber(mandate!.Label!),
            DateOnly.FromDateTime(AccountingPeriod.ToRomania(issuedAt!.Value)),
            string.Join(' ', mandatar.LastName!.Trim(), mandatar.FirstName!.Trim()),
            mandatar.Cnp!.Trim(),
            mandatar.Domicile!.Trim(),
            string.IsNullOrWhiteSpace(mandatar.Email) ? null : mandatar.Email.Trim(),
            phone);
        return (representative, mandate.DocumentId, null);
    }

    /// <summary>
    /// Numărul actului, cum intră în declarație: doar numărul, fără prefix și fără zerouri în față
    /// („ANAF-000123” → „123”). Câmpul are cel mult 10 caractere.
    /// </summary>
    internal static string ActNumber(string label)
    {
        string trimmed = label.Trim();

        // Împuternicirile generate aici sunt „ANAF-” urmat de număr; un act numerotat altfel rămâne cum e.
        const string prefix = "ANAF-";
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && trimmed[prefix.Length..].All(char.IsDigit))
        {
            string number = trimmed[prefix.Length..].TrimStart('0');
            if (number.Length is > 0 and <= 10)
            {
                return number;
            }
        }

        return trimmed.Length <= 10 ? trimmed : trimmed[^10..];
    }

    /// <summary>Arhiva atașată declarației: un singur fișier, împuternicirea.</summary>
    private static byte[] Zip(string fileName, byte[] content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry(string.IsNullOrWhiteSpace(fileName) ? "Imputernicire.pdf" : fileName).Open();
            entry.Write(content);
        }

        return stream.ToArray();
    }

    /// <summary>Ce împiedică generarea; <c>null</c> = datele sunt complete.</summary>
    private static string? Missing(AnafTaxpayer taxpayer)
    {
        if (string.IsNullOrEmpty(taxpayer.Cui))
        {
            return "Lipsește CUI-ul PFA-ului.";
        }

        if (!CuiValidator.Validate(taxpayer.Cui).IsValid)
        {
            return $"CUI-ul {taxpayer.Cui} nu e valid.";
        }

        return string.IsNullOrWhiteSpace(taxpayer.DeclarantLastName) || string.IsNullOrWhiteSpace(taxpayer.DeclarantFirstName)
            ? "Lipsește numele titularului."
            : null;
    }

    private static string Text(AnafValidatorMessage message) =>
        string.IsNullOrWhiteSpace(message.Field) ? message.Message : $"{message.Field}: {message.Message}";

    private static void Move(VatRegistrationRequest request, VatRegistrationStatus? from, VatRegistrationStatus to, Guid? userId, string? note)
    {
        List<VatRegistrationHistory> history = AccountingJson.Deserialize<List<VatRegistrationHistory>>(request.StatusHistoryJson, []);
        history.Add(new VatRegistrationHistory(from, to, DateTime.UtcNow, userId, note));
        request.StatusHistoryJson = AccountingJson.Serialize(history);
    }
}
