using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.FiscalProfiles;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.FiscalProfiles;

public sealed record GetFiscalProfileQuery(FiscalProfileScope Scope, Guid? PfaRegistrationId, int Year)
    : IQuery<FiscalProfileResponse>;

/// <summary>Autosalvarea ciornei, la fiecare „Continuă” și la închiderea formularului.</summary>
public sealed record SaveFiscalProfileDraftCommand(int Year, FiscalProfileAnswers Answers, int? ExpectedRevision)
    : ICommand<FiscalProfileResponse>;

/// <summary>Confirmarea PFA-ului: profilul trece în <c>COMPLETED</c> și estimările se deblochează.</summary>
public sealed record CompleteFiscalProfileCommand(int Year, FiscalProfileAnswers Answers, bool Confirmed, int? ExpectedRevision)
    : ICommand<FiscalProfileResponse>;

/// <summary>
/// O modificare după completare (PFA) sau oricând (staff, cu motiv). Nu schimbă statusul:
/// un profil completat rămâne completat, unul în ciornă rămâne în ciornă.
/// </summary>
public sealed record EditFiscalProfileCommand(
    FiscalProfileScope Scope,
    Guid? PfaRegistrationId,
    int Year,
    FiscalProfileAnswers Answers,
    string? Reason,
    int? ExpectedRevision) : ICommand<FiscalProfileResponse>;

public sealed record MarkFiscalProfilePromptShownCommand(int Year) : ICommand<FiscalProfileResponse>;

public sealed record GetEstimatedTaxesStatusQuery(int Year) : IQuery<EstimatedTaxesStatusResponse>;

public sealed record GetFiscalProfileRevisionsQuery(FiscalProfileScope Scope, Guid? PfaRegistrationId, int Year)
    : IQuery<IReadOnlyList<FiscalProfileRevisionResponse>>;

public sealed record CreateDataCorrectionCommand(int Year, string? Fields, string Details) : ICommand<DataCorrectionResponse>;

public sealed record ResolveDataCorrectionCommand(FiscalProfileScope Scope, Guid CorrectionId) : ICommand<DataCorrectionResponse>;

internal sealed class GetFiscalProfileQueryHandler(FiscalProfileService service)
    : IQueryHandler<GetFiscalProfileQuery, FiscalProfileResponse>
{
    public async Task<Result<FiscalProfileResponse>> Handle(GetFiscalProfileQuery query, CancellationToken cancellationToken)
    {
        if (!FiscalProfileService.IsValidYear(query.Year))
        {
            return Result.Failure<FiscalProfileResponse>(FiscalProfileService.InvalidYear);
        }

        Result<PfaRegistration> pfa = await service.ResolveAsync(query.Scope, query.PfaRegistrationId, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<FiscalProfileResponse>(pfa.Error);
        }

        PfaTaxProfile profile = await service.GetOrCreateAsync(pfa.Value, query.Year, cancellationToken);
        return await service.ToResponseAsync(pfa.Value, profile, query.Scope != FiscalProfileScope.Pfa, cancellationToken);
    }
}

internal sealed class SaveFiscalProfileDraftCommandHandler(FiscalProfileService service)
    : ICommandHandler<SaveFiscalProfileDraftCommand, FiscalProfileResponse>
{
    public async Task<Result<FiscalProfileResponse>> Handle(SaveFiscalProfileDraftCommand command, CancellationToken cancellationToken)
    {
        if (!FiscalProfileService.IsValidYear(command.Year))
        {
            return Result.Failure<FiscalProfileResponse>(FiscalProfileService.InvalidYear);
        }

        Result<PfaRegistration> pfa = await service.ResolveAsync(FiscalProfileScope.Pfa, null, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<FiscalProfileResponse>(pfa.Error);
        }

        PfaTaxProfile profile = await service.GetOrCreateAsync(pfa.Value, command.Year, cancellationToken);

        // După confirmare nu mai există ciornă: modificările trec prin editare, cu istoric.
        if (profile.Status == PfaTaxProfileStatus.Completed)
        {
            return await service.ToResponseAsync(pfa.Value, profile, false, cancellationToken);
        }

        Result revision = FiscalProfileService.CheckRevision(profile, command.ExpectedRevision);
        if (revision.IsFailure)
        {
            return Result.Failure<FiscalProfileResponse>(revision.Error);
        }

        FiscalProfileConditions conditions = await service.ConditionsAsync(pfa.Value, profile, cancellationToken);
        FiscalProfileAnswers answers = FiscalProfileSchema.Normalize(command.Answers ?? new FiscalProfileAnswers(), conditions);
        Dictionary<string, string> errors = FiscalProfileSchema.Validate(answers, conditions, requireAll: false);
        if (errors.Count > 0)
        {
            return Result.Failure<FiscalProfileResponse>(FiscalProfileService.ToValidationError(errors));
        }

        string json = FiscalProfileService.Serialize(answers);
        if (json != profile.AnswersJson || profile.Status == PfaTaxProfileStatus.NotStarted)
        {
            profile.AnswersJson = json;
            profile.Status = PfaTaxProfileStatus.Draft;
            service.Touch(profile);

            Result saved = await service.SaveAsync(cancellationToken);
            if (saved.IsFailure)
            {
                return Result.Failure<FiscalProfileResponse>(saved.Error);
            }
        }

        return await service.ToResponseAsync(pfa.Value, profile, false, cancellationToken);
    }
}

internal sealed class CompleteFiscalProfileCommandHandler(IApplicationDbContext context, FiscalProfileService service)
    : ICommandHandler<CompleteFiscalProfileCommand, FiscalProfileResponse>
{
    public async Task<Result<FiscalProfileResponse>> Handle(CompleteFiscalProfileCommand command, CancellationToken cancellationToken)
    {
        if (!FiscalProfileService.IsValidYear(command.Year))
        {
            return Result.Failure<FiscalProfileResponse>(FiscalProfileService.InvalidYear);
        }

        // Doar PFA-ul autentificat confirmă. Staff-ul n-are rută către handlerul ăsta.
        Result<PfaRegistration> pfa = await service.ResolveAsync(FiscalProfileScope.Pfa, null, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<FiscalProfileResponse>(pfa.Error);
        }

        PfaTaxProfile profile = await service.GetOrCreateAsync(pfa.Value, command.Year, cancellationToken);
        if (profile.Status == PfaTaxProfileStatus.Completed)
        {
            return Result.Failure<FiscalProfileResponse>(Error.Conflict(
                "FiscalProfile.AlreadyCompleted",
                "Profilul e deja completat. Modificările se fac din „Editează profilul”."));
        }

        Result revision = FiscalProfileService.CheckRevision(profile, command.ExpectedRevision);
        if (revision.IsFailure)
        {
            return Result.Failure<FiscalProfileResponse>(revision.Error);
        }

        FiscalProfileConditions conditions = await service.ConditionsAsync(pfa.Value, profile, cancellationToken);
        FiscalProfileAnswers answers = FiscalProfileSchema.Normalize(command.Answers ?? new FiscalProfileAnswers(), conditions);
        Dictionary<string, string> errors = FiscalProfileSchema.Validate(answers, conditions, requireAll: true);
        if (!command.Confirmed)
        {
            errors["confirmed"] = "Bifează confirmarea ca să activezi estimările.";
        }

        if (errors.Count > 0)
        {
            return Result.Failure<FiscalProfileResponse>(FiscalProfileService.ToValidationError(errors));
        }

        FiscalProfileAnswers before = FiscalProfileService.Deserialize(profile.AnswersJson);
        DateTime now = service.UtcNow;

        // Totul într-un singur SaveChanges, deci într-o singură tranzacție: statusul, deblocarea
        // estimărilor, revizia, închiderea sarcinii de apel și anunțul pentru contabil.
        profile.AnswersJson = FiscalProfileService.Serialize(answers);
        profile.Status = PfaTaxProfileStatus.Completed;
        profile.CompletedAtUtc = now;
        profile.EstimatedTaxesUnlockedAtUtc = now;
        service.Touch(profile);
        service.AddRevision(profile, FiscalProfileScope.Pfa, FiscalProfileSchema.Diff(before, answers), null);

        List<AdminCallTask> openTasks = await context.AdminCallTasks
            .Where(t => t.PfaRegistrationId == pfa.Value.Id
                && t.TaxYear == command.Year
                && (t.State == AdminCallTaskState.Open || t.State == AdminCallTaskState.Rescheduled))
            .ToListAsync(cancellationToken);
        foreach (AdminCallTask task in openTasks)
        {
            task.State = AdminCallTaskState.ResolvedByCompletion;
            task.ClosedAtUtc = now;
        }

        FiscalProfileCorrections.OpenIfNeeded(context, pfa.Value, profile, before, answers, now, onConfirmation: true);

        if (pfa.Value.AssignedContabilId is Guid contabilId)
        {
            string name = pfa.Value.LegalName ?? pfa.Value.HolderName ?? pfa.Value.FullName ?? "Un client";
            context.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = contabilId,
                Text = $"{name} a completat profilul fiscal {command.Year}.",
                Type = NotificationTypes.FiscalProfile,
                RelatedUserId = pfa.Value.UserId,
                DedupeKey = $"fiscal-profile-completed:{pfa.Value.Id}:{command.Year}",
                CreatedAtUtc = now,
            });
        }

        Result saved = await service.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<FiscalProfileResponse>(saved.Error);
        }

        // Estimările se calculează la cerere, din activitatea curentă și profilul ăsta, deci nu
        // e nimic de pus la coadă: dashboardul le arată de la următoarea citire.
        return await service.ToResponseAsync(pfa.Value, profile, false, cancellationToken);
    }
}

internal sealed class EditFiscalProfileCommandHandler(IApplicationDbContext context, FiscalProfileService service)
    : ICommandHandler<EditFiscalProfileCommand, FiscalProfileResponse>
{
    public async Task<Result<FiscalProfileResponse>> Handle(EditFiscalProfileCommand command, CancellationToken cancellationToken)
    {
        if (!FiscalProfileService.IsValidYear(command.Year))
        {
            return Result.Failure<FiscalProfileResponse>(FiscalProfileService.InvalidYear);
        }

        bool isStaff = command.Scope != FiscalProfileScope.Pfa;
        string? reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();

        if (isStaff && reason is null)
        {
            return Result.Failure<FiscalProfileResponse>(FiscalProfileService.ToValidationError(
                new Dictionary<string, string> { ["reason"] = "Scrie motivul modificării." }));
        }

        if (reason?.Length > 1000)
        {
            return Result.Failure<FiscalProfileResponse>(FiscalProfileService.ToValidationError(
                new Dictionary<string, string> { ["reason"] = "Motivul e prea lung (maximum 1000 de caractere)." }));
        }

        // Staff-ul nu scrie peste o versiune pe care n-a văzut-o.
        if (isStaff && command.ExpectedRevision is null)
        {
            return Result.Failure<FiscalProfileResponse>(Error.Problem(
                "FiscalProfile.RevisionRequired", "Lipsește revizia profilului (If-Match)."));
        }

        Result<PfaRegistration> pfa = await service.ResolveAsync(command.Scope, command.PfaRegistrationId, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<FiscalProfileResponse>(pfa.Error);
        }

        PfaTaxProfile profile = await service.GetOrCreateAsync(pfa.Value, command.Year, cancellationToken);
        if (!isStaff && profile.Status != PfaTaxProfileStatus.Completed)
        {
            return Result.Failure<FiscalProfileResponse>(Error.Unprocessable(
                "FiscalProfile.NotCompleted", "Profilul nu e încă completat. Continuă completarea și confirmă-l."));
        }

        Result revision = FiscalProfileService.CheckRevision(profile, command.ExpectedRevision);
        if (revision.IsFailure)
        {
            return Result.Failure<FiscalProfileResponse>(revision.Error);
        }

        FiscalProfileConditions conditions = await service.ConditionsAsync(pfa.Value, profile, cancellationToken);
        FiscalProfileAnswers answers = FiscalProfileSchema.Normalize(command.Answers ?? new FiscalProfileAnswers(), conditions);

        // Un profil completat rămâne complet după editare; o ciornă poate rămâne parțială.
        Dictionary<string, string> errors = FiscalProfileSchema.Validate(
            answers, conditions, requireAll: profile.Status == PfaTaxProfileStatus.Completed);
        if (errors.Count > 0)
        {
            return Result.Failure<FiscalProfileResponse>(FiscalProfileService.ToValidationError(errors));
        }

        FiscalProfileAnswers before = FiscalProfileService.Deserialize(profile.AnswersJson);
        List<FiscalProfileFieldChange> changes = FiscalProfileSchema.Diff(before, answers);
        if (changes.Count == 0)
        {
            return await service.ToResponseAsync(pfa.Value, profile, isStaff, cancellationToken);
        }

        profile.AnswersJson = FiscalProfileService.Serialize(answers);
        if (profile.Status == PfaTaxProfileStatus.NotStarted)
        {
            profile.Status = PfaTaxProfileStatus.Draft;
        }

        service.Touch(profile);
        service.AddRevision(profile, command.Scope, changes, reason);

        if (!isStaff)
        {
            FiscalProfileCorrections.OpenIfNeeded(context, pfa.Value, profile, before, answers, service.UtcNow, onConfirmation: false);
        }

        Result saved = await service.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<FiscalProfileResponse>(saved.Error);
        }

        return await service.ToResponseAsync(pfa.Value, profile, isStaff, cancellationToken);
    }
}

internal sealed class MarkFiscalProfilePromptShownCommandHandler(FiscalProfileService service)
    : ICommandHandler<MarkFiscalProfilePromptShownCommand, FiscalProfileResponse>
{
    public async Task<Result<FiscalProfileResponse>> Handle(MarkFiscalProfilePromptShownCommand command, CancellationToken cancellationToken)
    {
        if (!FiscalProfileService.IsValidYear(command.Year))
        {
            return Result.Failure<FiscalProfileResponse>(FiscalProfileService.InvalidYear);
        }

        Result<PfaRegistration> pfa = await service.ResolveAsync(FiscalProfileScope.Pfa, null, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<FiscalProfileResponse>(pfa.Error);
        }

        PfaTaxProfile profile = await service.GetOrCreateAsync(pfa.Value, command.Year, cancellationToken);
        if (profile.FirstPromptShownAtUtc is null)
        {
            profile.FirstPromptShownAtUtc = service.UtcNow;
            await service.SaveAsync(cancellationToken);
        }

        return await service.ToResponseAsync(pfa.Value, profile, false, cancellationToken);
    }
}

internal sealed class GetEstimatedTaxesStatusQueryHandler(IApplicationDbContext context, FiscalProfileService service)
    : IQueryHandler<GetEstimatedTaxesStatusQuery, EstimatedTaxesStatusResponse>
{
    public async Task<Result<EstimatedTaxesStatusResponse>> Handle(GetEstimatedTaxesStatusQuery query, CancellationToken cancellationToken)
    {
        if (!FiscalProfileService.IsValidYear(query.Year))
        {
            return Result.Failure<EstimatedTaxesStatusResponse>(FiscalProfileService.InvalidYear);
        }

        Result<PfaRegistration> pfa = await service.ResolveAsync(FiscalProfileScope.Pfa, null, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<EstimatedTaxesStatusResponse>(pfa.Error);
        }

        var profile = await context.PfaTaxProfiles
            .AsNoTracking()
            .Where(p => p.PfaRegistrationId == pfa.Value.Id && p.TaxYear == query.Year)
            .Select(p => new { p.Status, p.EstimatedTaxesUnlockedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);

        PfaTaxProfileStatus status = profile?.Status ?? PfaTaxProfileStatus.NotStarted;
        return new EstimatedTaxesStatusResponse(
            query.Year,
            status != PfaTaxProfileStatus.Completed,
            FiscalProfileService.StatusCode(status),
            profile?.EstimatedTaxesUnlockedAtUtc);
    }
}

internal sealed class GetFiscalProfileRevisionsQueryHandler(IApplicationDbContext context, FiscalProfileService service)
    : IQueryHandler<GetFiscalProfileRevisionsQuery, IReadOnlyList<FiscalProfileRevisionResponse>>
{
    public async Task<Result<IReadOnlyList<FiscalProfileRevisionResponse>>> Handle(
        GetFiscalProfileRevisionsQuery query,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> pfa = await service.ResolveAsync(query.Scope, query.PfaRegistrationId, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<IReadOnlyList<FiscalProfileRevisionResponse>>(pfa.Error);
        }

        List<PfaTaxProfileRevision> revisions = await context.PfaTaxProfileRevisions
            .AsNoTracking()
            .Where(r => r.Profile.PfaRegistrationId == pfa.Value.Id && r.Profile.TaxYear == query.Year)
            .OrderByDescending(r => r.Revision)
            .Take(100)
            .ToListAsync(cancellationToken);

        var actors = new Dictionary<Guid, FiscalProfileActor>();
        var result = new List<FiscalProfileRevisionResponse>(revisions.Count);
        foreach (PfaTaxProfileRevision revision in revisions)
        {
            if (!actors.TryGetValue(revision.ActorUserId, out FiscalProfileActor? actor))
            {
                actor = await service.ActorAsync(revision.ActorUserId, pfa.Value, cancellationToken);
                actors[revision.ActorUserId] = actor;
            }

            // Rolul din momentul modificării, nu cel de azi.
            actor = actor with { Role = revision.ActorRole };
            List<FiscalProfileFieldChange> changes =
                JsonSerializer.Deserialize<List<FiscalProfileFieldChange>>(revision.ChangesJson, FiscalProfileService.Json) ?? [];

            result.Add(new FiscalProfileRevisionResponse(revision.Revision, revision.CreatedAtUtc, actor, changes, revision.Reason));
        }

        return result;
    }
}

internal sealed class CreateDataCorrectionCommandHandler(IApplicationDbContext context, FiscalProfileService service)
    : ICommandHandler<CreateDataCorrectionCommand, DataCorrectionResponse>
{
    public async Task<Result<DataCorrectionResponse>> Handle(CreateDataCorrectionCommand command, CancellationToken cancellationToken)
    {
        string details = command.Details?.Trim() ?? string.Empty;
        if (details.Length is 0 or > 2000)
        {
            return Result.Failure<DataCorrectionResponse>(FiscalProfileService.ToValidationError(
                new Dictionary<string, string> { ["details"] = "Descrie ce trebuie corectat (maximum 2000 de caractere)." }));
        }

        Result<PfaRegistration> pfa = await service.ResolveAsync(FiscalProfileScope.Pfa, null, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<DataCorrectionResponse>(pfa.Error);
        }

        Guid? profileId = await context.PfaTaxProfiles
            .Where(p => p.PfaRegistrationId == pfa.Value.Id && p.TaxYear == command.Year)
            .Select(p => (Guid?)p.Id)
            .SingleOrDefaultAsync(cancellationToken);

        var correction = new PfaDataCorrectionRequest
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfa.Value.Id,
            ProfileId = profileId,
            Fields = string.IsNullOrWhiteSpace(command.Fields)
                ? FiscalProfileCorrections.AccountData
                : command.Fields.Trim()[..Math.Min(command.Fields.Trim().Length, 256)],
            Details = details,
            CreatedAtUtc = service.UtcNow,
        };
        context.PfaDataCorrectionRequests.Add(correction);
        await context.SaveChangesAsync(cancellationToken);

        return FiscalProfileCorrections.ToResponse(correction);
    }
}

internal sealed class ResolveDataCorrectionCommandHandler(IApplicationDbContext context, FiscalProfileService service)
    : ICommandHandler<ResolveDataCorrectionCommand, DataCorrectionResponse>
{
    public async Task<Result<DataCorrectionResponse>> Handle(ResolveDataCorrectionCommand command, CancellationToken cancellationToken)
    {
        PfaDataCorrectionRequest? correction = await context.PfaDataCorrectionRequests
            .SingleOrDefaultAsync(c => c.Id == command.CorrectionId, cancellationToken);
        if (correction is null || command.Scope == FiscalProfileScope.Pfa)
        {
            return Result.Failure<DataCorrectionResponse>(FiscalProfileService.PfaNotFound);
        }

        // Aceeași regulă de acces ca pentru profil: adminul oricare, contabilul doar clienții lui.
        Result<PfaRegistration> pfa = await service.ResolveAsync(command.Scope, correction.PfaRegistrationId, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<DataCorrectionResponse>(pfa.Error);
        }

        if (correction.State != DataCorrectionState.Resolved)
        {
            correction.State = DataCorrectionState.Resolved;
            correction.ResolvedAtUtc = service.UtcNow;
            correction.ResolvedByUserId = service.CallerId;
            await context.SaveChangesAsync(cancellationToken);
        }

        return FiscalProfileCorrections.ToResponse(correction);
    }
}

internal static class FiscalProfileCorrections
{
    public const string AccountData = "Datele PFA";

    public static DataCorrectionResponse ToResponse(PfaDataCorrectionRequest c) =>
        new(c.Id, c.Fields, c.Details, c.State.ToString(), c.CreatedAtUtc, c.ResolvedAtUtc);

    /// <summary>
    /// „Nu, trebuie corectate” de la pasul 1 devine o cerere de corectare pentru echipă. Sursa
    /// datelor nu se atinge. La confirmare se deschide mereu (ciorna n-a deschis nimic); la o
    /// editare, doar dacă textul s-a schimbat.
    /// </summary>
    public static void OpenIfNeeded(
        IApplicationDbContext context,
        PfaRegistration pfa,
        PfaTaxProfile profile,
        FiscalProfileAnswers before,
        FiscalProfileAnswers after,
        DateTime nowUtc,
        bool onConfirmation)
    {
        if (after.DataCorrect != FiscalProfileSchema.No || string.IsNullOrWhiteSpace(after.CorrectionDetails))
        {
            return;
        }

        bool unchanged = before.DataCorrect == FiscalProfileSchema.No
            && string.Equals(before.CorrectionDetails, after.CorrectionDetails, StringComparison.Ordinal);
        if (unchanged && !onConfirmation)
        {
            return;
        }

        context.PfaDataCorrectionRequests.Add(new PfaDataCorrectionRequest
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfa.Id,
            ProfileId = profile.Id,
            Fields = AccountData,
            Details = after.CorrectionDetails,
            CreatedAtUtc = nowUtc,
        });
    }
}
