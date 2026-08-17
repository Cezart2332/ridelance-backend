using Application.Abstractions.Data;
using Application.Abstractions.Dossiers;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Arr;

/// <summary>Pasul 3 — generează dosarul PDF ARR și îl salvează ca document criptat.</summary>
public sealed record GenerateArrDossierCommand(Guid UserId) : ICommand<ArrStateResponse>;

internal sealed class GenerateArrDossierCommandHandler(
    IApplicationDbContext context,
    IDossierGenerator dossierGenerator,
    IFileEncryptionService fileEncryptionService,
    OnboardingStateService stateService)
    : ICommandHandler<GenerateArrDossierCommand, ArrStateResponse>
{
    public async Task<Result<ArrStateResponse>> Handle(
        GenerateArrDossierCommand command,
        CancellationToken cancellationToken)
    {
        // Poarta RL-01: se scrie doar pe pasul activ. Prima verificare din handler —
        // altfel am valida conținutul unei cereri care oricum nu are voie să treacă.
        Result guard = await stateService.EnsureWritableAsync(
            command.UserId, OnboardingStepKey.Arr, cancellationToken);

        if (guard.IsFailure)
        {
            return Result.Failure<ArrStateResponse>(guard.Error);
        }

        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.ArrAuthorizationRequest)
            .Include(r => r.User)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration?.ArrAuthorizationRequest is null)
        {
            return Result.Failure<ArrStateResponse>(ArrShared.NotFound);
        }

        ArrAuthorizationRequest request = registration.ArrAuthorizationRequest;

        // Documentele deja încărcate (non-respinse) care satisfac cerințele pasului ARR. Sunt
        // atașate în dosar, nu doar bifate — de asta le încărcăm întregi, nu doar categoria.
        IReadOnlyList<DossierAttachment> included = await DossierAttachments.CollectAsync(
            context,
            fileEncryptionService,
            command.UserId,
            OnboardingSectionCatalog.RequirementsFor(OnboardingSectionKey.AutorizatieTransport),
            cancellationToken);

        if (UserDisplayName.IsMissing(registration.User))
        {
            return Result.Failure<ArrStateResponse>(ArrShared.ApplicantNameMissing);
        }

        string applicantName = UserDisplayName.Of(registration.User);
        string? address = BuildAddress(registration);
        DateTime nowUtc = DateTime.UtcNow;

        var data = new ArrDossierData(
            applicantName,
            registration.Cui,
            registration.LegalName,
            address,
            request.AgencyName,
            request.FeeSnapshotBani,
            included,
            nowUtc,
            // Dosarele produse într-o sesiune de test poartă filigran, ca să nu ajungă la ghișeu.
            registration.IsDevSession);

        byte[] pdf = dossierGenerator.GenerateArrDossier(data);

        string cuiPart = string.IsNullOrWhiteSpace(registration.Cui) ? "PFA" : registration.Cui;
        string fileName = $"Dosar_Autorizatie_Transport_Alternativ_{cuiPart}_{nowUtc:yyyyMMdd}.pdf";
        string storedFileName = $"{Guid.NewGuid()}.pdf";

        using var stream = new MemoryStream(pdf);
        EncryptedFileResult encrypted = await fileEncryptionService.EncryptAndSaveAsync(
            stream, storedFileName, cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            PfaRegistrationId = registration.Id,
            OriginalFileName = fileName,
            StoredFileName = storedFileName,
            ContentType = "application/pdf",
            Category = DocumentCategory.DosarAutorizatieArr,
            // RL-07 — dosarul îl producem noi, deci nu apare în lista șoferului.
            Origin = DocumentOrigin.SystemGenerated,
            Status = DocumentStatus.Verified,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = pdf.Length,
            UploadedAtUtc = nowUtc,
            IssuedAtUtc = nowUtc,
            AiStatus = DocumentAiStatus.None,
        };
        context.Documents.Add(document);

        request.DossierDocumentId = document.Id;
        request.DossierGeneratedAtUtc = nowUtc;
        if (request.Status == ArrAuthorizationStatus.Draft)
        {
            request.Status = ArrAuthorizationStatus.DossierGenerated;
        }
        request.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(ArrShared.ToResponse(request));
    }

    private static string? BuildAddress(PfaRegistration r)
    {
        string[] parts = new[]
            {
                string.IsNullOrWhiteSpace(r.Street) ? null : $"{r.Street} {r.Number}".Trim(),
                r.City,
                r.County,
            }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToArray();

        return parts.Length == 0 ? null : string.Join(", ", parts);
    }
}
