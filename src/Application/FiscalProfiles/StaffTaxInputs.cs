using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.FiscalEstimates;
using Domain.FiscalProfiles;
using Domain.PfaRegistrations;
using SharedKernel;

namespace Application.FiscalProfiles;

/// <summary>
/// Ce completează contabilul din evidența lui, pentru calculul taxelor. PFA-ul răspunde în profil
/// doar cu Da/Nu; când spune „Da”, suma de care depinde calculul o trece contabilul aici.
/// </summary>
/// <param name="OtherIncomeCassInsured"><c>yes</c> / <c>no</c>; <c>null</c> = necompletat.</param>
public sealed record StaffTaxInputs(
    decimal? OtherIndependentNetAnnual,
    string? OtherIncomeCassInsured,
    decimal? CarriedLossesAmount,
    decimal? CassOptInBase);

/// <summary>
/// Datele contabilului și care dintre ele contează: <c>Ask…</c> e adevărat când PFA-ul a răspuns
/// „Da” la întrebarea de care ține suma.
/// </summary>
public sealed record StaffTaxInputsResponse(
    int TaxYear,
    int Revision,
    bool AskOtherIndependentNetAnnual,
    decimal? OtherIndependentNetAnnual,
    bool AskOtherIncomeCassInsured,
    string? OtherIncomeCassInsured,
    bool AskCarriedLossesAmount,
    decimal? CarriedLossesAmount,
    bool AskCassOptInBase,
    decimal? CassOptInBase);

public sealed record GetStaffTaxInputsQuery(FiscalProfileScope Scope, Guid PfaRegistrationId, int Year) : IQuery<StaffTaxInputsResponse>;

public sealed record SaveStaffTaxInputsCommand(
    FiscalProfileScope Scope,
    Guid PfaRegistrationId,
    int Year,
    StaffTaxInputs Inputs,
    int? ExpectedRevision) : ICommand<StaffTaxInputsResponse>;

internal static class StaffTaxInputsMapping
{
    public static readonly Error StaffOnly =
        Error.Problem("StaffTaxInputs.StaffOnly", "Datele acestea le completează contabilul.");

    public static StaffTaxInputsResponse ToResponse(PfaTaxProfile profile, FiscalProfileAnswers a) => new(
        profile.TaxYear,
        profile.Revision,
        a.OtherIndependent == FiscalProfileSchema.Yes,
        a.OtherIndependentNetAnnual,
        a.OtherIncome == FiscalProfileSchema.Yes,
        a.OtherIncomeCassInsured,
        a.CarriedLosses == FiscalProfileSchema.Yes,
        a.CarriedLossesAmount,
        a.CassOptIn == FiscalProfileSchema.Yes,
        a.CassOptInBase);
}

internal sealed class GetStaffTaxInputsQueryHandler(FiscalProfileService service)
    : IQueryHandler<GetStaffTaxInputsQuery, StaffTaxInputsResponse>
{
    public async Task<Result<StaffTaxInputsResponse>> Handle(GetStaffTaxInputsQuery query, CancellationToken cancellationToken)
    {
        if (!FiscalProfileService.IsValidYear(query.Year))
        {
            return Result.Failure<StaffTaxInputsResponse>(FiscalProfileService.InvalidYear);
        }

        if (query.Scope == FiscalProfileScope.Pfa)
        {
            return Result.Failure<StaffTaxInputsResponse>(StaffTaxInputsMapping.StaffOnly);
        }

        Result<PfaRegistration> pfa = await service.ResolveAsync(query.Scope, query.PfaRegistrationId, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<StaffTaxInputsResponse>(pfa.Error);
        }

        PfaTaxProfile profile = await service.GetOrCreateAsync(pfa.Value, query.Year, cancellationToken);
        Result saved = await service.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<StaffTaxInputsResponse>(saved.Error);
        }

        return StaffTaxInputsMapping.ToResponse(profile, FiscalProfileService.Deserialize(profile.AnswersJson));
    }
}

internal sealed class SaveStaffTaxInputsCommandHandler(IApplicationDbContext context, FiscalProfileService service)
    : ICommandHandler<SaveStaffTaxInputsCommand, StaffTaxInputsResponse>
{
    public async Task<Result<StaffTaxInputsResponse>> Handle(SaveStaffTaxInputsCommand command, CancellationToken cancellationToken)
    {
        if (!FiscalProfileService.IsValidYear(command.Year))
        {
            return Result.Failure<StaffTaxInputsResponse>(FiscalProfileService.InvalidYear);
        }

        if (command.Scope == FiscalProfileScope.Pfa)
        {
            return Result.Failure<StaffTaxInputsResponse>(StaffTaxInputsMapping.StaffOnly);
        }

        Result<PfaRegistration> pfa = await service.ResolveAsync(command.Scope, command.PfaRegistrationId, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<StaffTaxInputsResponse>(pfa.Error);
        }

        PfaTaxProfile profile = await service.GetOrCreateAsync(pfa.Value, command.Year, cancellationToken);
        Result revision = FiscalProfileService.CheckRevision(profile, command.ExpectedRevision);
        if (revision.IsFailure)
        {
            return Result.Failure<StaffTaxInputsResponse>(revision.Error);
        }

        FiscalProfileAnswers before = FiscalProfileService.Deserialize(profile.AnswersJson);
        StaffTaxInputs inputs = command.Inputs;
        FiscalProfileConditions conditions = await service.ConditionsAsync(pfa.Value, profile, cancellationToken);

        // Normalizarea păstrează o sumă doar cât PFA-ul a spus „Da” la întrebarea ei.
        FiscalProfileAnswers after = FiscalProfileSchema.Normalize(
            before with
            {
                OtherIndependentNetAnnual = inputs.OtherIndependentNetAnnual,
                OtherIncomeCassInsured = inputs.OtherIncomeCassInsured,
                CarriedLossesAmount = inputs.CarriedLossesAmount,
                CassOptInBase = inputs.CassOptInBase,
            },
            conditions);

        Dictionary<string, string> errors = FiscalProfileSchema.Validate(after, conditions, requireAll: false);
        if (errors.Count > 0)
        {
            return Result.Failure<StaffTaxInputsResponse>(FiscalProfileService.ToValidationError(errors));
        }

        List<FiscalProfileFieldChange> changes = FiscalProfileSchema.Diff(before, after);
        if (changes.Count > 0)
        {
            profile.AnswersJson = FiscalProfileService.Serialize(after);
            service.Touch(profile);
            service.AddRevision(profile, command.Scope, changes, "Date completate de contabil");
            await FiscalEstimateInvalidation.MarkStaleAsync(context, pfa.Value.Id, command.Year, service.UtcNow, cancellationToken);

            Result saved = await service.SaveAsync(cancellationToken);
            if (saved.IsFailure)
            {
                return Result.Failure<StaffTaxInputsResponse>(saved.Error);
            }
        }

        return StaffTaxInputsMapping.ToResponse(profile, after);
    }
}
