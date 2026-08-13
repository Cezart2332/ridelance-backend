using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Domain.Documents;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

/// <summary>Un acord dat în wizard. Textul nu vine de la client — se ia din fluxul activ.</summary>
public sealed record ConsentPayload(string StepKey);

public sealed record SignaturePayload(
    string? SignatureImage,
    string? SignatureVector,
    int CanvasWidth,
    int CanvasHeight,
    IReadOnlyList<ConsentPayload>? Consents);

/// <summary>
/// Contextul cererii, citit din HttpContext de endpoint. Nimic din ce urmează nu se acceptă
/// de la client: un semnatar nu-și poate proba singur propria semnătură.
/// </summary>
public sealed record SignatureContext(string? IpAddress, string? UserAgent, string? IdempotencyKey);

/// <summary>
/// Etapa 3 — consimțămintele și semnătura, trimise atomic. Spre deosebire de etapele 1 și 2,
/// aici nu există salvare parțială: fie rămân în DB toate cele cinci acorduri plus semnătura,
/// fie niciunul.
/// </summary>
public sealed record SignCompanyFormationCommand(
    Guid UserId,
    SignaturePayload Payload,
    SignatureContext Context)
    : ICommand<CompanyFormationResponse>;

internal sealed class SignCompanyFormationCommandHandler(
    IApplicationDbContext context,
    ISecretProtector secretProtector,
    IFileEncryptionService fileEncryptionService,
    OnboardingStateService stateService)
    : ICommandHandler<SignCompanyFormationCommand, CompanyFormationResponse>
{
    private const string ConsentContext = "infiintare-societate";
    private const int MaxSignatureBytes = 2 * 1024 * 1024;

    public async Task<Result<CompanyFormationResponse>> Handle(
        SignCompanyFormationCommand command,
        CancellationToken cancellationToken)
    {
        // Poarta RL-01: se scrie doar pe pasul activ. Prima verificare din handler —
        // altfel am valida conținutul unei cereri care oricum nu are voie să treacă.
        Result guard = await stateService.EnsureWritableAsync(
            command.UserId, OnboardingStepKey.Pfa, cancellationToken);

        if (guard.IsFailure)
        {
            return Result.Failure<CompanyFormationResponse>(guard.Error);
        }

        // Retrimiterea aceleiași chei întoarce rezultatul original — un dublu-click pe
        // „Confirmă semnătura" nu are voie să creeze a doua cerere.
        if (!string.IsNullOrWhiteSpace(command.Context.IdempotencyKey))
        {
            CompanyFormationRequest? alreadySigned = await FindByIdempotencyKeyAsync(
                command.Context.IdempotencyKey, cancellationToken);

            if (alreadySigned is not null)
            {
                return Result.Success(
                    CompanyFormationMapper.ToResponse(alreadySigned, secretProtector, revealCnp: true));
            }
        }

        Result<CompanyFormationRequest> loaded = await CompanyFormationLoader.ForUserAsync(
            context, command.UserId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<CompanyFormationResponse>(loaded.Error);
        }

        CompanyFormationRequest request = loaded.Value;

        if (!request.PersonalDataComplete)
        {
            return Result.Failure<CompanyFormationResponse>(CompanyFormationErrors.PersonalDataIncomplete);
        }

        if (!request.RegisteredOfficeComplete)
        {
            return Result.Failure<CompanyFormationResponse>(CompanyFormationErrors.RegisteredOfficeIncomplete);
        }

        LegalConsentFlow? flow = await context.LegalConsentFlows
            .AsNoTracking()
            .Include(f => f.Steps)
            .Where(f => f.Context == ConsentContext && f.IsActive)
            .OrderByDescending(f => f.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (flow is null)
        {
            return Result.Failure<CompanyFormationResponse>(CompanyFormationErrors.ConsentFlowNotFound);
        }

        // Acordul e valid doar dacă acoperă toți pașii versiunii active. Un pas adăugat de
        // juridic invalidează automat un client vechi care nu-l cunoaște.
        HashSet<string> accepted = [.. (command.Payload.Consents ?? []).Select(c => c.StepKey)];
        if (flow.Steps.Exists(s => !accepted.Contains(s.Key)))
        {
            return Result.Failure<CompanyFormationResponse>(CompanyFormationErrors.ConsentIncomplete);
        }

        Result<byte[]> image = DecodePng(command.Payload.SignatureImage);
        if (image.IsFailure)
        {
            return Result.Failure<CompanyFormationResponse>(image.Error);
        }

        DateTime signedAtUtc = DateTime.UtcNow;

        var consents = flow.Steps
            .OrderBy(s => s.Position)
            .Select(s => new CompanyFormationConsent
            {
                Id = Guid.NewGuid(),
                CompanyFormationRequestId = request.Id,
                StepKey = s.Key,
                Version = flow.Version,
                TextSnapshot = s.Body,
                CheckboxLabelSnapshot = s.CheckboxLabel,
                AcceptedAtUtc = signedAtUtc,
            })
            .ToList();

        // Un dosar redeschis (InfoRequested) își pierde acordurile vechi: datele s-au schimbat,
        // deci hash-ul lor nu mai corespunde.
        context.CompanyFormationConsents.RemoveRange(request.Consents);
        request.Consents.Clear();

        // Prin DbSet, nu doar prin colecția de navigație: entitățile noi au deja Id setat, iar
        // EF le-ar considera existente și ar emite UPDATE în loc de INSERT.
        context.CompanyFormationConsents.AddRange(consents);
        request.Consents.AddRange(consents);

        Guid? imageDocumentId = await StoreSignatureImageAsync(request, command.UserId, image.Value, cancellationToken);

        var agent = UserAgentInfo.Parse(command.Context.UserAgent);

        var signature = new CompanyFormationSignature
        {
            Id = Guid.NewGuid(),
            CompanyFormationRequestId = request.Id,
            ImageDocumentId = imageDocumentId,
            VectorData = command.Payload.SignatureVector,
            CanvasWidth = command.Payload.CanvasWidth,
            CanvasHeight = command.Payload.CanvasHeight,
            IpAddress = command.Context.IpAddress,
            UserAgent = Truncate(command.Context.UserAgent, 512),
            DeviceType = agent.DeviceType,
            Os = agent.Os,
            Browser = agent.Browser,
            SignedAtUtc = signedAtUtc,
            PayloadHash = ComputePayloadHash(request, consents, image.Value),
            IdempotencyKey = string.IsNullOrWhiteSpace(command.Context.IdempotencyKey)
                ? null
                : command.Context.IdempotencyKey,
        };

        if (request.Signature is not null)
        {
            context.CompanyFormationSignatures.Remove(request.Signature);
        }

        context.CompanyFormationSignatures.Add(signature);
        request.Signature = signature;

        request.Status = CompanyFormationStatus.Submitted;
        request.CurrentStage = CompanyFormationStage.Consent;
        request.SubmittedAtUtc = signedAtUtc;
        request.UpdatedAtUtc = signedAtUtc;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(
            CompanyFormationMapper.ToResponse(request, secretProtector, revealCnp: true));
    }

    private async Task<CompanyFormationRequest?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        Guid? requestId = await context.CompanyFormationSignatures
            .AsNoTracking()
            .Where(s => s.IdempotencyKey == idempotencyKey)
            .Select(s => (Guid?)s.CompanyFormationRequestId)
            .FirstOrDefaultAsync(cancellationToken);

        if (requestId is null)
        {
            return null;
        }

        return await context.CompanyFormationRequests
            .AsNoTracking()
            .Include(r => r.Owners)
            .Include(r => r.Signature)
            .FirstOrDefaultAsync(r => r.Id == requestId.Value, cancellationToken);
    }

    /// <summary>Imaginea semnăturii se stochează criptat, ca orice document din dosar.</summary>
    private async Task<Guid?> StoreSignatureImageAsync(
        CompanyFormationRequest request,
        Guid userId,
        byte[] png,
        CancellationToken cancellationToken)
    {
        string storedFileName = $"{Guid.NewGuid()}.png";

        using var stream = new MemoryStream(png, writable: false);
        EncryptedFileResult encrypted = await fileEncryptionService.EncryptAndSaveAsync(
            stream, storedFileName, cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PfaRegistrationId = request.PfaRegistrationId,
            OriginalFileName = "specimen-semnatura.png",
            StoredFileName = storedFileName,
            ContentType = "image/png",
            Category = DocumentCategory.SpecimenSemnatura,
            Status = DocumentStatus.Pending,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = png.Length,
            UploadedAtUtc = DateTime.UtcNow,
            // Specimenul nu trece prin OCR: nu are ce citi din el.
            AiStatus = DocumentAiStatus.None,
        };

        context.Documents.Add(document);
        return document.Id;
    }

    /// <summary>
    /// SHA-256 peste imagine plus datele semnate. Dovada că semnătura aparține <em>acestui</em>
    /// set exact de date: dacă adminul redeschide dosarul și datele se schimbă, hash-ul
    /// recalculat nu mai corespunde.
    /// </summary>
    private static string ComputePayloadHash(
        CompanyFormationRequest request,
        IReadOnlyList<CompanyFormationConsent> consents,
        byte[] image)
    {
        var canonical = new StringBuilder();

        AppendPersoana(canonical, request.Solicitant);
        canonical.Append(request.OfficeType).Append('|')
            .Append(request.ConsultoOfficeId).Append('|')
            .Append(request.IsOwner).Append('|');
        AppendAdresa(canonical, request.OfficeAddress);

        foreach (CompanyFormationOwner owner in request.Owners.OrderBy(o => o.Position))
        {
            AppendPersoana(canonical, owner.Persoana);
        }

        foreach (CompanyFormationConsent consent in consents)
        {
            canonical.Append(consent.StepKey).Append('|')
                .Append(consent.Version).Append('|')
                .Append(consent.TextSnapshot).Append('|');
        }

        byte[] payload = [.. Encoding.UTF8.GetBytes(canonical.ToString()), .. image];
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    private static void AppendPersoana(StringBuilder builder, PersoanaFizica p)
    {
        // CNP-ul intră în hash criptat, nu în clar: hash-ul ajunge în export și în admin.
        builder.Append(p.Nume).Append('|')
            .Append(p.Prenume).Append('|')
            .Append(p.CnpEncrypted).Append('|')
            .Append(p.TipAct).Append('|')
            .Append(p.SerieAct).Append('|')
            .Append(p.NumarAct).Append('|')
            .Append(p.AutoritateEmitenta).Append('|')
            .Append(p.DataEmiterii?.ToString("O", CultureInfo.InvariantCulture)).Append('|')
            .Append(p.DataExpirarii?.ToString("O", CultureInfo.InvariantCulture)).Append('|');
        AppendAdresa(builder, p.Domiciliu);
    }

    private static void AppendAdresa(StringBuilder builder, Adresa a) =>
        builder.Append(a.Judet).Append('|')
            .Append(a.Localitate).Append('|')
            .Append(a.Strada).Append('|')
            .Append(a.Numar).Append('|')
            .Append(a.Bloc).Append('|')
            .Append(a.Scara).Append('|')
            .Append(a.Etaj).Append('|')
            .Append(a.Apartament).Append('|');

    /// <summary>Acceptă atât data URL-ul trimis de canvas, cât și base64 curat.</summary>
    private static Result<byte[]> DecodePng(string? signatureImage)
    {
        if (string.IsNullOrWhiteSpace(signatureImage))
        {
            return Result.Failure<byte[]>(CompanyFormationErrors.SignatureMissing);
        }

        int comma = signatureImage.IndexOf(',', StringComparison.Ordinal);
        string base64 = comma >= 0 ? signatureImage[(comma + 1)..] : signatureImage;

        if (base64.Length > MaxSignatureBytes)
        {
            return Result.Failure<byte[]>(CompanyFormationErrors.SignatureTooLarge);
        }

        Span<byte> buffer = new byte[base64.Length];
        if (!Convert.TryFromBase64String(base64, buffer, out int written) || written == 0)
        {
            return Result.Failure<byte[]>(CompanyFormationErrors.SignatureMissing);
        }

        return Result.Success(buffer[..written].ToArray());
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}

/// <summary>
/// Dispozitivul de pe care s-a semnat, dedus din User-Agent. E probatoriu, nu logică de
/// produs: ne interesează ce scrie în antet, nu adevărul absolut despre browser.
/// </summary>
internal sealed record UserAgentInfo(string? DeviceType, string? Os, string? Browser)
{
    private static readonly (string Token, string Name)[] OperatingSystems =
    [
        ("Android", "Android"),
        ("iPhone", "iOS"),
        ("iPad", "iOS"),
        ("iOS", "iOS"),
        ("Windows", "Windows"),
        ("Mac OS X", "macOS"),
        ("Linux", "Linux"),
    ];

    private static readonly (string Token, string Name)[] Browsers =
    [
        ("Edg/", "Edge"),
        ("OPR/", "Opera"),
        ("Opera", "Opera"),
        ("Firefox", "Firefox"),
        ("Chrome", "Chrome"),
        ("CriOS", "Chrome"),
        ("Safari", "Safari"),
    ];

    public static UserAgentInfo Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return new UserAgentInfo(null, null, null);
        }

        bool Has(string token) => userAgent.Contains(token, StringComparison.OrdinalIgnoreCase);

        string device = "Desktop";
        if (Has("iPad") || Has("Tablet"))
        {
            device = "Tablet";
        }
        else if (Has("Mobi") || Has("Android") || Has("iPhone"))
        {
            device = "Mobile";
        }

        string? os = null;
        foreach ((string token, string name) in OperatingSystems)
        {
            if (Has(token))
            {
                os = name;
                break;
            }
        }

        // Ordinea contează: Edge și Opera se dau drept Chrome, Chrome se dă drept Safari.
        string? browser = null;
        foreach ((string token, string name) in Browsers)
        {
            if (Has(token))
            {
                browser = name;
                break;
            }
        }

        return new UserAgentInfo(device, os, browser);
    }
}
