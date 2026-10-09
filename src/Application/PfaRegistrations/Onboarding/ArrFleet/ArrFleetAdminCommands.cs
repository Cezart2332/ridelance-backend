using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Documents;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.ArrFleet;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.ArrFleet;

/// <summary>Pasul, cum îl vede adminul: aceleași date ca la client.</summary>
public sealed record GetAdminArrFleetQuery(Guid RegistrationId) : IQuery<ArrFleetStateResponse>;

/// <summary>Adminul avansează procedura. Fiecare schimbare se scrie în jurnal.</summary>
public sealed record ChangeArrFleetStatusCommand(Guid RegistrationId, Guid ReviewerUserId, ArrFleetStatus Status)
    : ICommand<ArrFleetStateResponse>;

/// <summary>Adminul redeschide pasul pentru corecturi; motivul îl vede clientul.</summary>
public sealed record ReopenArrFleetCommand(Guid RegistrationId, Guid ReviewerUserId, string Reason)
    : ICommand<ArrFleetStateResponse>;

/// <summary>
/// Un document oficial obținut de agent. Tipul decide categoria, deci documentul ajunge singur în
/// dashboardul clientului, la locul lui — nu există încărcare generică.
/// </summary>
public sealed record UploadArrFleetOfficialDocumentCommand(
    Guid RegistrationId,
    Guid ReviewerUserId,
    ArrFleetOfficialDocument Type,
    string FileName,
    string ContentType,
    Stream FileStream,
    long FileSize,
    string? DocumentNumber,
    DateTime? IssuedAtUtc,
    DateTime? ExpiresAtUtc) : ICommand<ArrFleetStateResponse>;

internal sealed class GetAdminArrFleetQueryHandler(ArrFleetService service)
    : IQueryHandler<GetAdminArrFleetQuery, ArrFleetStateResponse>
{
    public async Task<Result<ArrFleetStateResponse>> Handle(GetAdminArrFleetQuery query, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await service.LoadByIdAsync(query.RegistrationId, cancellationToken);

        return registration is null
            ? Result.Failure<ArrFleetStateResponse>(PfaRegistrationErrors.NotFound(query.RegistrationId))
            : await service.ToResponseAsync(registration, cancellationToken);
    }
}

internal sealed class ChangeArrFleetStatusCommandHandler(IApplicationDbContext context, ArrFleetService service)
    : ICommandHandler<ChangeArrFleetStatusCommand, ArrFleetStateResponse>
{
    public async Task<Result<ArrFleetStateResponse>> Handle(ChangeArrFleetStatusCommand command, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await service.LoadByIdAsync(command.RegistrationId, cancellationToken);
        if (registration is null)
        {
            return Result.Failure<ArrFleetStateResponse>(PfaRegistrationErrors.NotFound(command.RegistrationId));
        }

        if (registration.ArrFleetApplication is not { SubmittedAtUtc: not null } application || command.Status == ArrFleetStatus.Draft)
        {
            return Result.Failure<ArrFleetStateResponse>(ArrFleetErrors.NotSubmitted);
        }

        List<Document> documents = await service.DocumentsAsync(registration.UserId, cancellationToken);
        if (ArrFleetRules.MissingOfficialDocument(application, command.Status, documents) is string label)
        {
            return Result.Failure<ArrFleetStateResponse>(ArrFleetErrors.OfficialDocumentMissing(label));
        }

        DateTime nowUtc = DateTime.UtcNow;
        service.SetStatus(application, command.Status, command.ReviewerUserId, nowUtc);

        // Finalizat: conturile de flotă sunt active, iar dashboardul le arată ca atare.
        if (command.Status == ArrFleetStatus.Completed)
        {
            foreach (PfaPlatformAccount account in registration.PlatformAccounts.Where(a => a.IsSelectedByUser))
            {
                account.OnboardingStatus = PfaPlatformOnboardingStatus.Active;
                account.UpdatedAtUtc = nowUtc;
            }
        }

        context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = registration.UserId,
            Text = $"ARR & Cont Flotă: {ArrFleetRules.StatusLabel(command.Status)}.",
            Type = NotificationTypes.OnboardingStepUpdate,
            IsRead = false,
            CreatedAtUtc = nowUtc,
        });

        await context.SaveChangesAsync(cancellationToken);
        return await service.ToResponseAsync(registration, cancellationToken);
    }
}

internal sealed class ReopenArrFleetCommandHandler(IApplicationDbContext context, ArrFleetService service)
    : ICommandHandler<ReopenArrFleetCommand, ArrFleetStateResponse>
{
    public async Task<Result<ArrFleetStateResponse>> Handle(ReopenArrFleetCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<ArrFleetStateResponse>(ArrFleetErrors.ReasonRequired);
        }

        PfaRegistration? registration = await service.LoadByIdAsync(command.RegistrationId, cancellationToken);
        if (registration is null)
        {
            return Result.Failure<ArrFleetStateResponse>(PfaRegistrationErrors.NotFound(command.RegistrationId));
        }

        if (registration.ArrFleetApplication is not { SubmittedAtUtc: not null } application)
        {
            return Result.Failure<ArrFleetStateResponse>(ArrFleetErrors.NotSubmitted);
        }

        DateTime nowUtc = DateTime.UtcNow;
        application.SubmittedAtUtc = null;
        application.ReopenedReason = command.Reason.Trim();
        application.ReopenedAtUtc = nowUtc;
        service.SetStatus(application, ArrFleetStatus.Draft, command.ReviewerUserId, nowUtc);

        context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = registration.UserId,
            Text = $"Pasul „ARR & Cont Flotă” a fost redeschis: {application.ReopenedReason}",
            Type = NotificationTypes.OnboardingStepUpdate,
            IsRead = false,
            CreatedAtUtc = nowUtc,
        });

        await context.SaveChangesAsync(cancellationToken);
        return await service.ToResponseAsync(registration, cancellationToken);
    }
}

internal sealed class UploadArrFleetOfficialDocumentCommandHandler(
    IApplicationDbContext context,
    ArrFleetService service,
    IFileEncryptionService fileEncryptionService)
    : ICommandHandler<UploadArrFleetOfficialDocumentCommand, ArrFleetStateResponse>
{
    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "application/pdf", "image/jpeg", "image/png" };

    public async Task<Result<ArrFleetStateResponse>> Handle(
        UploadArrFleetOfficialDocumentCommand command,
        CancellationToken cancellationToken)
    {
        if (!AllowedContentTypes.Contains(command.ContentType))
        {
            return Result.Failure<ArrFleetStateResponse>(DocumentErrors.InvalidFileType);
        }

        if (command.FileSize > Documents.Upload.UploadDocumentCommandHandler.MaxFileSize)
        {
            return Result.Failure<ArrFleetStateResponse>(DocumentErrors.FileTooLarge);
        }

        PfaRegistration? registration = await service.LoadByIdAsync(command.RegistrationId, cancellationToken);
        if (registration is null)
        {
            return Result.Failure<ArrFleetStateResponse>(PfaRegistrationErrors.NotFound(command.RegistrationId));
        }

        if (registration.ArrFleetApplication is not ArrFleetApplication application)
        {
            return Result.Failure<ArrFleetStateResponse>(ArrFleetErrors.NotFound);
        }

        // Ecusonul unei platforme pe care clientul n-a ales-o n-ar avea unde să fie folosit.
        bool badgeForOtherPlatform = command.Type switch
        {
            ArrFleetOfficialDocument.UberBadge => !application.Has(ArrFleetPlatforms.Uber),
            ArrFleetOfficialDocument.BoltBadge => !application.Has(ArrFleetPlatforms.Bolt),
            _ => false,
        };

        if (badgeForOtherPlatform)
        {
            return Result.Failure<ArrFleetStateResponse>(ArrFleetErrors.BadgePlatformNotSelected);
        }

        DateTime nowUtc = DateTime.UtcNow;
        DateTime issuedAt = command.IssuedAtUtc ?? nowUtc;
        string storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(command.FileName)}";
        EncryptedFileResult encrypted = await fileEncryptionService.EncryptAndSaveAsync(
            command.FileStream, storedFileName, cancellationToken);

        DocumentCategory category = ArrFleetRules.CategoryOf(command.Type);

        // Versiunea anterioară a aceluiași act rămâne în dosar, dar nu mai e cea valabilă.
        List<Document> previous = await context.Documents
            .Where(d => d.UserId == registration.UserId && d.Category == category && !d.IsSuperseded)
            .ToListAsync(cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = registration.UserId,
            PfaRegistrationId = registration.Id,
            OriginalFileName = command.FileName,
            StoredFileName = storedFileName,
            ContentType = command.ContentType,
            Category = category,
            Origin = DocumentOrigin.AdminUpload,
            Status = DocumentStatus.Verified,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = command.FileSize,
            UploadedAtUtc = nowUtc,
            IssuedAtUtc = issuedAt,
            ExpiresAtUtc = command.ExpiresAtUtc ?? ArrFleetRules.DefaultExpiry(command.Type, issuedAt),
            DocumentNumber = string.IsNullOrWhiteSpace(command.DocumentNumber) ? null : command.DocumentNumber.Trim(),
            AiStatus = DocumentAiStatus.None,
        };
        context.Documents.Add(document);

        foreach (Document old in previous)
        {
            old.IsSuperseded = true;
            old.ReplacedByDocumentId = document.Id;
        }

        context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = registration.UserId,
            Text = $"Ai un document nou în dashboard: {ArrFleetRules.LabelOf(command.Type)}.",
            Type = NotificationTypes.DocumentStatusUpdate,
            IsRead = false,
            CreatedAtUtc = nowUtc,
        });

        await context.SaveChangesAsync(cancellationToken);
        return await service.ToResponseAsync(registration, cancellationToken);
    }
}
