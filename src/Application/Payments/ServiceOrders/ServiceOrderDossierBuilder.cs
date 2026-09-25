using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Application.Documents.ExtractedFields;
using Application.PfaRegistrations.Onboarding.CompanyFormation;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Payments.ServiceOrders;

/// <summary>Un fișier trimis din formular, ca data URL sau base64 curat.</summary>
public sealed record ServiceFilePayload(string? FileName, string? ContentType, string? Data);

/// <summary>
/// Formularul unui serviciu, trimis întreg odată cu plata. Aceleași câmpuri ca în onboarding;
/// fără salvare parțială — cine cumpără de pe site nu are cont în care să rămână un draft.
/// </summary>
public sealed record ServiceDossierPayload(
    PersoanaFizicaPayload? Solicitant,
    RegisteredOfficePayload? Office,
    string? CompanyName,
    string? CompanyCui,
    ServiceFilePayload? IdentityDocument,
    SignaturePayload? Signature);

public static class ServiceOrderErrors
{
    public static readonly Error DossierMissing = Error.Problem(
        "ServiceOrder.DossierMissing", "Completează datele cerute pentru acest serviciu.");

    public static readonly Error IdentityDocumentMissing = Error.Problem(
        "ServiceOrder.IdentityDocumentMissing", "Încarcă o poză sau un PDF cu actul de identitate.");

    public static readonly Error IdentityDocumentInvalid = Error.Problem(
        "ServiceOrder.IdentityDocumentInvalid", "Actul de identitate trebuie să fie o poză (JPG, PNG, WebP) sau un PDF de cel mult 10 MB.");

    public static readonly Error OfficeRequired = Error.Problem(
        "ServiceOrder.OfficeRequired", "Alege zona în care vrei sediul social.");
}

/// <summary>
/// Validează formularul unui serviciu și îl transformă în dosar: CNP-ul criptat, fișierele
/// salvate criptat, acordurile cu textul versiunii active și probatoriul semnăturii.
///
/// Regulile sunt cele din onboarding — prin <see cref="ServiceOrderDossier.ToFormationRequest"/>,
/// nu rescrise aici.
/// </summary>
public sealed class ServiceOrderDossierBuilder(
    IApplicationDbContext context,
    ISecretProtector secretProtector,
    IFileEncryptionService fileEncryptionService)
{
    private const string ConsentContext = "infiintare-societate";
    private const int MinDaysBeforeExpiry = 30;
    private const long MaxIdentityDocumentBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> IdentityDocumentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["application/pdf"] = ".pdf",
    };

    public async Task<Result<ServiceOrderDossier>> BuildAsync(
        string serviceKey,
        ServiceDossierPayload? payload,
        SignatureContext context,
        CancellationToken cancellationToken)
    {
        ServiceDossierKind? kind = ServiceDossierKinds.For(serviceKey);
        if (kind is null)
        {
            return Result.Failure<ServiceOrderDossier>(Error.Problem("Service.InvalidKey", "Serviciul selectat nu este disponibil."));
        }

        if (payload?.Solicitant is null)
        {
            return Result.Failure<ServiceOrderDossier>(ServiceOrderErrors.DossierMissing);
        }

        var dossier = new ServiceOrderDossier
        {
            IncludesVatIntracom = ServiceDossierKinds.IncludesVatIntracom(serviceKey),
        };

        Result solicitant = ApplyPerson(dossier.Solicitant, payload.Solicitant);
        if (solicitant.IsFailure)
        {
            return Result.Failure<ServiceOrderDossier>(solicitant.Error);
        }

        Result office = kind == ServiceDossierKind.Formation
            ? await ApplyFormationOfficeAsync(dossier, payload.Office, cancellationToken)
            : await ApplyHostedOfficeAsync(dossier, payload, cancellationToken);

        if (office.IsFailure)
        {
            return Result.Failure<ServiceOrderDossier>(office.Error);
        }

        CompanyFormationRequest check = dossier.ToFormationRequest(Guid.NewGuid(), null);

        if (!check.PersonalDataComplete)
        {
            return Result.Failure<ServiceOrderDossier>(CompanyFormationErrors.PersonalDataIncomplete);
        }

        if (!check.RegisteredOfficeComplete)
        {
            return Result.Failure<ServiceOrderDossier>(CompanyFormationErrors.RegisteredOfficeIncomplete);
        }

        Result<ServiceOrderFile> identity = await StoreIdentityDocumentAsync(payload.IdentityDocument, cancellationToken);
        if (identity.IsFailure)
        {
            return Result.Failure<ServiceOrderDossier>(identity.Error);
        }

        dossier.IdentityDocument = identity.Value;

        if (kind == ServiceDossierKind.Formation)
        {
            Result signed = await SignAsync(dossier, payload.Signature, context, cancellationToken);
            if (signed.IsFailure)
            {
                return Result.Failure<ServiceOrderDossier>(signed.Error);
            }
        }

        return Result.Success(dossier);
    }

    private Result ApplyPerson(PersoanaFizica target, PersoanaFizicaPayload payload)
    {
        string? cnp = Digits(payload.Cnp);
        if (cnp is not { Length: 13 } || !CnpValidator.IsValid(cnp))
        {
            return Result.Failure(CompanyFormationErrors.InvalidCnp);
        }

        if (payload.DataExpirarii is DateOnly expires
            && expires < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(MinDaysBeforeExpiry))
        {
            return Result.Failure(CompanyFormationErrors.ExpiredIdentityCard);
        }

        CompanyFormationMapper.Apply(target, payload, secretProtector, PrefilledFieldMap.Untracked());
        return Result.Success();
    }

    /// <summary>Sediul, exact ca în onboarding: o zonă Consulto sau o adresă proprie, cu proprietarii ei.</summary>
    private async Task<Result> ApplyFormationOfficeAsync(
        ServiceOrderDossier dossier,
        RegisteredOfficePayload? payload,
        CancellationToken cancellationToken)
    {
        if (payload is null)
        {
            return Result.Failure(CompanyFormationErrors.RegisteredOfficeIncomplete);
        }

        RegisteredOfficeType? type = Enum.TryParse(payload.Type, ignoreCase: true, out RegisteredOfficeType parsed)
            && Enum.IsDefined(parsed)
                ? parsed
                : null;

        dossier.OfficeType = type;

        if (type == RegisteredOfficeType.ConsultoProvided)
        {
            Result office = await EnsureOfficeAsync(payload.ConsultoOfficeId, cancellationToken);
            if (office.IsFailure)
            {
                return office;
            }

            dossier.ConsultoOfficeId = payload.ConsultoOfficeId;
            return Result.Success();
        }

        dossier.IsOwner = payload.IsOwner;
        CompanyFormationMapper.Apply(dossier.OfficeAddress, payload.Adresa);
        dossier.AcknowledgedOwnershipDocs = payload.AcknowledgedOwnershipDocs;
        dossier.AcknowledgedSubmitLater = payload.AcknowledgedSubmitLater;
        dossier.AcknowledgedOwnerConsent = payload.AcknowledgedOwnerConsent;

        // Proprietarii contează doar când imobilul nu e al solicitantului.
        if (payload.IsOwner == false)
        {
            foreach (OwnerPayload owner in payload.Owners ?? [])
            {
                var persoana = new PersoanaFizica();
                Result applied = ApplyPerson(persoana, owner.Persoana);
                if (applied.IsFailure)
                {
                    return applied;
                }

                if (!persoana.IsComplete)
                {
                    return Result.Failure(CompanyFormationErrors.OwnerRequired);
                }

                dossier.Owners.Add(persoana);
            }
        }

        return Result.Success();
    }

    /// <summary>Găzduirea: titularul, PFA-ul (dacă există) și zona aleasă din lista Consulto.</summary>
    private async Task<Result> ApplyHostedOfficeAsync(
        ServiceOrderDossier dossier,
        ServiceDossierPayload payload,
        CancellationToken cancellationToken)
    {
        Guid? officeId = payload.Office?.ConsultoOfficeId;
        if (officeId is null)
        {
            return Result.Failure(ServiceOrderErrors.OfficeRequired);
        }

        Result office = await EnsureOfficeAsync(officeId, cancellationToken);
        if (office.IsFailure)
        {
            return office;
        }

        dossier.OfficeType = RegisteredOfficeType.ConsultoProvided;
        dossier.ConsultoOfficeId = officeId;
        dossier.CompanyName = Trim(payload.CompanyName, 256);
        dossier.CompanyCui = Trim(payload.CompanyCui, 32);
        return Result.Success();
    }

    private async Task<Result> EnsureOfficeAsync(Guid? officeId, CancellationToken cancellationToken)
    {
        if (officeId is null)
        {
            return Result.Failure(ServiceOrderErrors.OfficeRequired);
        }

        bool exists = await context.ConsultoOffices
            .AnyAsync(o => o.Id == officeId && o.IsActive, cancellationToken);

        return exists ? Result.Success() : Result.Failure(CompanyFormationErrors.ConsultoOfficeNotFound);
    }

    private async Task<Result<ServiceOrderFile>> StoreIdentityDocumentAsync(
        ServiceFilePayload? file,
        CancellationToken cancellationToken)
    {
        if (file is null || string.IsNullOrWhiteSpace(file.Data))
        {
            return Result.Failure<ServiceOrderFile>(ServiceOrderErrors.IdentityDocumentMissing);
        }

        string contentType = file.ContentType?.Trim() ?? string.Empty;
        if (!IdentityDocumentTypes.TryGetValue(contentType, out string? extension))
        {
            return Result.Failure<ServiceOrderFile>(ServiceOrderErrors.IdentityDocumentInvalid);
        }

        byte[]? bytes = DecodeBase64(file.Data, MaxIdentityDocumentBytes);
        if (bytes is null)
        {
            return Result.Failure<ServiceOrderFile>(ServiceOrderErrors.IdentityDocumentInvalid);
        }

        string originalName = string.IsNullOrWhiteSpace(file.FileName)
            ? $"act-identitate{extension}"
            : Path.GetFileName(file.FileName.Trim());

        return Result.Success(await StoreAsync(bytes, originalName, contentType, extension, cancellationToken));
    }

    /// <summary>Acordurile versiunii active și semnătura, cu același hash ca în onboarding.</summary>
    private async Task<Result> SignAsync(
        ServiceOrderDossier dossier,
        SignaturePayload? payload,
        SignatureContext signatureContext,
        CancellationToken cancellationToken)
    {
        if (payload is null)
        {
            return Result.Failure(CompanyFormationErrors.SignatureMissing);
        }

        LegalConsentFlow? flow = await context.LegalConsentFlows
            .AsNoTracking()
            .Include(f => f.Steps)
            .Where(f => f.Context == ConsentContext && f.IsActive)
            .OrderByDescending(f => f.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (flow is null)
        {
            return Result.Failure(CompanyFormationErrors.ConsentFlowNotFound);
        }

        HashSet<string> accepted = [.. (payload.Consents ?? []).Select(c => c.StepKey)];
        if (flow.Steps.Exists(s => !accepted.Contains(s.Key)))
        {
            return Result.Failure(CompanyFormationErrors.ConsentIncomplete);
        }

        Result<byte[]> image = SignCompanyFormationCommandHandler.DecodePng(payload.SignatureImage);
        if (image.IsFailure)
        {
            return Result.Failure(image.Error);
        }

        DateTime signedAtUtc = DateTime.UtcNow;

        dossier.Consents = flow.Steps
            .OrderBy(s => s.Position)
            .Select(s => new ServiceOrderConsent
            {
                StepKey = s.Key,
                Version = flow.Version,
                TextSnapshot = s.Body,
                CheckboxLabelSnapshot = s.CheckboxLabel,
                AcceptedAtUtc = signedAtUtc,
            })
            .ToList();

        CompanyFormationRequest request = dossier.ToFormationRequest(Guid.NewGuid(), null);
        var agent = UserAgentInfo.Parse(signatureContext.UserAgent);

        dossier.Signature = new ServiceOrderSignature
        {
            Image = await StoreAsync(image.Value, "specimen-semnatura.png", "image/png", ".png", cancellationToken),
            VectorData = payload.SignatureVector,
            CanvasWidth = payload.CanvasWidth,
            CanvasHeight = payload.CanvasHeight,
            IpAddress = signatureContext.IpAddress,
            UserAgent = Trim(signatureContext.UserAgent, 512),
            DeviceType = agent.DeviceType,
            Os = agent.Os,
            Browser = agent.Browser,
            SignedAtUtc = signedAtUtc,
            PayloadHash = SignCompanyFormationCommandHandler.ComputePayloadHash(request, request.Consents, image.Value),
        };

        return Result.Success();
    }

    private async Task<ServiceOrderFile> StoreAsync(
        byte[] content,
        string originalName,
        string contentType,
        string extension,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        EncryptedFileResult encrypted = await fileEncryptionService.EncryptAndSaveAsync(
            stream, $"{Guid.NewGuid()}{extension}", cancellationToken);

        return new ServiceOrderFile
        {
            OriginalFileName = originalName,
            ContentType = contentType,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = content.Length,
        };
    }

    private static byte[]? DecodeBase64(string data, long maxBytes)
    {
        int comma = data.IndexOf(',', StringComparison.Ordinal);
        string base64 = comma >= 0 ? data[(comma + 1)..] : data;

        // Base64 are 4 caractere la fiecare 3 octeți: plafonul se verifică înainte de decodare.
        if (base64.Length > maxBytes / 3 * 4 + 4)
        {
            return null;
        }

        byte[] buffer = new byte[base64.Length];
        return Convert.TryFromBase64String(base64, buffer, out int written) && written > 0
            ? buffer[..written]
            : null;
    }

    private static string? Digits(string? value) =>
        value is null ? null : new string(value.Where(char.IsAsciiDigit).ToArray());

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
