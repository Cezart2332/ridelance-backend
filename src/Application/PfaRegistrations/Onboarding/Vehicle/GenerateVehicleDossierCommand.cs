using Application.Abstractions.Data;
using Application.Abstractions.Dossiers;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Vehicle;

/// <summary>Pasul 5 — generează dosarul PDF copie conformă & ecusoane și îl salvează criptat.</summary>
public sealed record GenerateVehicleDossierCommand(Guid UserId) : ICommand<VehicleStateResponse>;

internal sealed class GenerateVehicleDossierCommandHandler(
    IApplicationDbContext context,
    IDossierGenerator dossierGenerator,
    IFileEncryptionService fileEncryptionService,
    OnboardingStateService stateService)
    : ICommandHandler<GenerateVehicleDossierCommand, VehicleStateResponse>
{
    public async Task<Result<VehicleStateResponse>> Handle(
        GenerateVehicleDossierCommand command,
        CancellationToken cancellationToken)
    {
        // Poarta RL-01: se scrie doar pe pasul activ. Prima verificare din handler —
        // altfel am valida conținutul unei cereri care oricum nu are voie să treacă.
        Result guard = await stateService.EnsureWritableAsync(
            command.UserId, OnboardingStepKey.Vehicle, cancellationToken);

        if (guard.IsFailure)
        {
            return Result.Failure<VehicleStateResponse>(guard.Error);
        }

        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.User)
            .Include(r => r.Vehicles).ThenInclude(v => v.CopyRequest)
            .Include(r => r.Vehicles).ThenInclude(v => v.Badges)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure<VehicleStateResponse>(VehicleShared.NoRegistration);
        }

        PfaVehicle? vehicle = VehicleShared.PrimaryVehicle(registration);
        if (vehicle is null)
        {
            return Result.Failure<VehicleStateResponse>(VehicleShared.VehicleNotFound);
        }

        if (vehicle.CopyRequest is null)
        {
            return Result.Failure<VehicleStateResponse>(VehicleShared.CopyRequestNotFound);
        }

        VehicleCopyRequest copy = vehicle.CopyRequest;
        DateTime nowUtc = DateTime.UtcNow;

        // Cerințele celor două secțiuni, deduplicate pe etichetă (Talon/ITP și contractul apar în
        // ambele), cu documentele încărcate atașate efectiv în dosar.
        //
        // Partea de mașină vine prin `RequirementsForVehicle`, ca ramura de leasing să-și aducă
        // în dosar și acordul finanțatorului, nu doar contractul (spec fix-uri §11.2/§11.3).
        var requirements = OnboardingSectionCatalog
            .RequirementsFor(OnboardingSectionKey.CopieConforma)
            .Concat(OnboardingSectionCatalog.RequirementsForVehicle(vehicle.OwnershipMode))
            .DistinctBy(req => req.Label)
            .ToList();

        IReadOnlyList<DossierAttachment> included = await DossierAttachments.CollectAsync(
            context, fileEncryptionService, command.UserId, requirements, cancellationToken);

        var badgeLines = vehicle.Badges
            .OrderBy(b => b.Provider)
            .Select(b => new VehicleBadgeLine(b.Provider.ToString(), b.SetCount, b.TotalFeeSnapshotBani))
            .ToList();
        long badgesTotal = vehicle.Badges.Sum(b => b.TotalFeeSnapshotBani);

        if (UserDisplayName.IsMissing(registration.User))
        {
            return Result.Failure<VehicleStateResponse>(VehicleShared.ApplicantNameMissing);
        }

        string applicantName = UserDisplayName.Of(registration.User);
        string? vehicleDescription = BuildVehicleDescription(vehicle);

        var data = new VehicleDossierData(
            applicantName,
            registration.Cui,
            registration.LegalName,
            vehicle.PlateNumber,
            vehicle.Vin,
            vehicleDescription,
            copy.Years,
            copy.FeePerYearSnapshotBani,
            copy.TotalFeeSnapshotBani,
            badgeLines,
            badgesTotal,
            included,
            nowUtc,
            // Dosarele produse într-o sesiune de test poartă filigran, ca să nu ajungă la ghișeu.
            registration.IsDevSession);

        byte[] pdf = dossierGenerator.GenerateVehicleDossier(data);

        string platePart = string.IsNullOrWhiteSpace(vehicle.PlateNumber) ? "vehicul" : vehicle.PlateNumber;
        string cuiPart = string.IsNullOrWhiteSpace(registration.Cui) ? "PFA" : registration.Cui;
        string fileName = $"Dosar_Copie_Conforma_Ecusoane_{platePart}_{cuiPart}.pdf";
        string storedFileName = $"{Guid.NewGuid()}.pdf";

        using var stream = new MemoryStream(pdf);
        EncryptedFileResult encrypted = await fileEncryptionService.EncryptAndSaveAsync(
            stream, storedFileName, cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            PfaRegistrationId = registration.Id,
            PfaVehicleId = vehicle.Id,
            OriginalFileName = fileName,
            StoredFileName = storedFileName,
            ContentType = "application/pdf",
            Category = DocumentCategory.DosarCopieConformaEcusoane,
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

        copy.DossierDocumentId = document.Id;
        copy.DossierGeneratedAtUtc = nowUtc;
        if (copy.Status == VehicleCopyRequestStatus.Draft)
        {
            copy.Status = VehicleCopyRequestStatus.DossierGenerated;
        }
        copy.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(VehicleShared.ToResponse(vehicle, copy.FeePerYearSnapshotBani,
            vehicle.Badges.Count > 0 ? vehicle.Badges[0].FeePerSetSnapshotBani : VehicleShared.DefaultBadgeFeePerSetBani));
    }

    private static string? BuildVehicleDescription(PfaVehicle vehicle)
    {
        string? year = vehicle.FirstRegistrationYear?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string[] parts = new[] { vehicle.Make, vehicle.Model, year }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToArray();

        return parts.Length == 0 ? null : string.Join(" ", parts);
    }
}
