using System.Globalization;
using Application.Abstractions;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Notifications;
using Domain.Documents;
using Domain.PfaRegistrations.CompanyFormation;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

/// <summary>
/// Acordul proprietarului sediului, când imobilul nu e al solicitantului. Proprietarul n-are cont:
/// primește pe email un link (token neghicibil, păstrat hash-uit, valabil 7 zile, folosit o dată),
/// citește declarația și semnează cu degetul sau cu mouse-ul.
/// </summary>
internal static class OwnerConsentText
{
    public static string Build(CompanyFormationRequest request, CompanyFormationOwner owner) =>
        $"Subsemnatul/Subsemnata {FullName(owner.Persoana)}, în calitate de proprietar al imobilului situat în " +
        $"{ExportCompanyFormationQueryHandler.FormatAddress(request.OfficeAddress)}, îmi exprim acordul ca imobilul să fie " +
        $"folosit ca sediu profesional de {FullName(request.Solicitant)}, pentru înregistrarea și desfășurarea activității " +
        "sale ca persoană fizică autorizată (PFA).";

    public static string FullName(PersoanaFizica p) =>
        string.Join(' ', new[] { p.Prenume, p.Nume }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
}

public static class OwnerConsentErrors
{
    public static readonly Error LinkInvalid = Error.NotFound(
        "OwnerConsent.LinkInvalid", "Linkul nu mai e valabil. Cere solicitantului să-ți retrimită acordul.");

    public static readonly Error AlreadySigned = Error.Conflict(
        "OwnerConsent.AlreadySigned", "Acordul a fost deja semnat.");

    public static readonly Error EmailMissing = Error.Problem(
        "OwnerConsent.EmailMissing", "Proprietarul nu are o adresă de email validă.");

    public static readonly Error SignatureInvalid = Error.Problem(
        "OwnerConsent.SignatureInvalid", "Semnătura nu a putut fi citită.");
}

/// <summary>Trimite acordul pe email. Retrimiterea face un token nou și îl anulează pe cel vechi.</summary>
public sealed class OwnerConsentSender(
    IApplicationDbContext context,
    IEmailService emailService,
    IMjmlRenderer mjml,
    IConfiguration configuration,
    ILogger<OwnerConsentSender> logger)
{
    /// <summary>
    /// Trimite tuturor proprietarilor care n-au semnat. Un email căzut nu oprește semnarea
    /// dosarului: se poate retrimite din admin.
    /// </summary>
    public async Task SendPendingAsync(CompanyFormationRequest request, CancellationToken cancellationToken)
    {
        if (request.OfficeType != RegisteredOfficeType.Own || request.IsOwner != false)
        {
            return;
        }

        foreach (CompanyFormationOwner owner in request.Owners.Where(o => o.ConsentSignedAtUtc is null))
        {
            try
            {
                await SendAsync(request, owner, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Acordul proprietarului {OwnerId} nu a putut fi trimis.", owner.Id);
            }
        }
    }

    public async Task<Result> SendAsync(CompanyFormationRequest request, CompanyFormationOwner owner, CancellationToken cancellationToken)
    {
        if (owner.ConsentSignedAtUtc is not null)
        {
            return Result.Failure(OwnerConsentErrors.AlreadySigned);
        }

        if (!CompanyFormationOwner.IsValidEmail(owner.Email))
        {
            return Result.Failure(OwnerConsentErrors.EmailMissing);
        }

        string token = SignatureToken.Create();
        DateTime nowUtc = DateTime.UtcNow;
        owner.ConsentTokenHash = SignatureToken.Hash(token);
        owner.ConsentSentAtUtc = nowUtc;
        owner.ConsentExpiresAtUtc = nowUtc.Add(SignatureToken.Lifetime);
        await context.SaveChangesAsync(cancellationToken);

        string baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        var link = new Uri($"{baseUrl}/acord-proprietar/{token}");
        string applicant = OwnerConsentText.FullName(request.Solicitant);

        string html = mjml.Render(EmailTemplates.Notice(
            "Acordul tău pentru sediul profesional",
            OwnerConsentText.FullName(owner.Persoana),
            [
                $"{applicant} a declarat imobilul tău ca sediu profesional pentru PFA și are nevoie de acordul tău semnat.",
                "Deschide linkul, citește declarația și semnează. Linkul e valabil șapte zile și poate fi folosit o singură dată. Nu ai nevoie de cont.",
            ],
            highlight: null,
            "Citește și semnează",
            link));

        await emailService.SendEmailAsync(owner.Email!.Trim(), "Acord proprietar pentru sediul profesional — RIDElance", html, cancellationToken);
        return Result.Success();
    }
}

public sealed record OwnerConsentRequestDto(
    string OwnerName,
    string ApplicantName,
    string OfficeAddress,
    string ConsentText,
    DateTime ExpiresAtUtc);

public sealed record GetOwnerConsentQuery(string Token) : IQuery<OwnerConsentRequestDto>;

public sealed record SignOwnerConsentCommand(string Token, string SignatureImage, string? IpAddress, string? UserAgent) : ICommand;

/// <summary>Admin: retrimite acordul unui proprietar.</summary>
public sealed record ResendOwnerConsentCommand(Guid PfaRegistrationId, Guid OwnerId) : ICommand;

internal static class OwnerConsentLookup
{
    public static async Task<Result<CompanyFormationOwner>> FindAsync(
        IApplicationDbContext context, string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return Result.Failure<CompanyFormationOwner>(OwnerConsentErrors.LinkInvalid);
        }

        string hash = SignatureToken.Hash(token);
        CompanyFormationOwner? owner = await context.CompanyFormationOwners
            .Include(o => o.CompanyFormationRequest)
            .FirstOrDefaultAsync(o => o.ConsentTokenHash == hash, cancellationToken);

        if (owner is null || owner.ConsentExpiresAtUtc is not DateTime expires || expires <= DateTime.UtcNow)
        {
            return Result.Failure<CompanyFormationOwner>(OwnerConsentErrors.LinkInvalid);
        }

        return owner.ConsentSignedAtUtc is not null
            ? Result.Failure<CompanyFormationOwner>(OwnerConsentErrors.AlreadySigned)
            : Result.Success(owner);
    }
}

internal sealed class GetOwnerConsentQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetOwnerConsentQuery, OwnerConsentRequestDto>
{
    public async Task<Result<OwnerConsentRequestDto>> Handle(GetOwnerConsentQuery query, CancellationToken cancellationToken)
    {
        Result<CompanyFormationOwner> found = await OwnerConsentLookup.FindAsync(context, query.Token, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<OwnerConsentRequestDto>(found.Error);
        }

        CompanyFormationOwner owner = found.Value;
        CompanyFormationRequest request = owner.CompanyFormationRequest;
        return new OwnerConsentRequestDto(
            OwnerConsentText.FullName(owner.Persoana),
            OwnerConsentText.FullName(request.Solicitant),
            ExportCompanyFormationQueryHandler.FormatAddress(request.OfficeAddress),
            OwnerConsentText.Build(request, owner),
            owner.ConsentExpiresAtUtc!.Value);
    }
}

internal sealed class SignOwnerConsentCommandHandler(IApplicationDbContext context, IFileEncryptionService encryption)
    : ICommandHandler<SignOwnerConsentCommand>
{
    private const int MaxSignatureBytes = 2 * 1024 * 1024;

    public async Task<Result> Handle(SignOwnerConsentCommand command, CancellationToken cancellationToken)
    {
        Result<CompanyFormationOwner> found = await OwnerConsentLookup.FindAsync(context, command.Token, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        byte[] png;
        try
        {
            string payload = command.SignatureImage ?? string.Empty;
            int comma = payload.IndexOf(',', StringComparison.Ordinal);
            png = Convert.FromBase64String(comma >= 0 ? payload[(comma + 1)..] : payload);
        }
        catch (FormatException)
        {
            return Result.Failure(OwnerConsentErrors.SignatureInvalid);
        }

        if (png.Length == 0 || png.Length > MaxSignatureBytes)
        {
            return Result.Failure(OwnerConsentErrors.SignatureInvalid);
        }

        CompanyFormationOwner owner = found.Value;
        CompanyFormationRequest request = owner.CompanyFormationRequest;
        Guid? applicantUserId = await context.PfaRegistrations
            .Where(r => r.Id == request.PfaRegistrationId)
            .Select(r => (Guid?)r.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (applicantUserId is null)
        {
            return Result.Failure(OwnerConsentErrors.LinkInvalid);
        }

        string fileName = string.Create(CultureInfo.InvariantCulture, $"acord-proprietar-{owner.Position + 1}-{Guid.NewGuid():N}.png");
        using var stream = new MemoryStream(png, writable: false);
        EncryptedFileResult encrypted = await encryption.EncryptAndSaveAsync(stream, fileName, cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = applicantUserId.Value,
            PfaRegistrationId = request.PfaRegistrationId,
            OriginalFileName = $"semnatura-proprietar-{owner.Position + 1}.png",
            StoredFileName = fileName,
            ContentType = "image/png",
            Category = DocumentCategory.SpecimenSemnatura,
            Status = DocumentStatus.Verified,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = png.Length,
            UploadedAtUtc = DateTime.UtcNow,
            AiStatus = DocumentAiStatus.None,
        };
        context.Documents.Add(document);

        DateTime nowUtc = DateTime.UtcNow;
        owner.ConsentSignedAtUtc = nowUtc;
        owner.ConsentSignatureDocumentId = document.Id;
        owner.ConsentTextSnapshot = OwnerConsentText.Build(request, owner);
        owner.ConsentIpAddress = command.IpAddress?.Length > 64 ? command.IpAddress[..64] : command.IpAddress;
        owner.ConsentUserAgent = command.UserAgent?.Length > 512 ? command.UserAgent[..512] : command.UserAgent;
        // Linkul se consumă: un email trimis mai departe nu mai poate semna a doua oară.
        owner.ConsentTokenHash = null;
        owner.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ResendOwnerConsentCommandHandler(IApplicationDbContext context, OwnerConsentSender sender)
    : ICommandHandler<ResendOwnerConsentCommand>
{
    public async Task<Result> Handle(ResendOwnerConsentCommand command, CancellationToken cancellationToken)
    {
        CompanyFormationRequest? request = await context.CompanyFormationRequests
            .Include(r => r.Owners)
            .FirstOrDefaultAsync(r => r.PfaRegistrationId == command.PfaRegistrationId, cancellationToken);
        CompanyFormationOwner? owner = request?.Owners.Find(o => o.Id == command.OwnerId);
        if (request is null || owner is null)
        {
            return Result.Failure(CompanyFormationErrors.OwnerNotFound);
        }

        return await sender.SendAsync(request, owner, cancellationToken);
    }
}
