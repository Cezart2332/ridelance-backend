using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.FiscalProfiles;
using Domain.FiscalEstimates;
using Domain.FiscalProfiles;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Application.FiscalEstimates;

/// <summary>Calculează acum taxele estimate ale unui PFA pentru un an și salvează o rulare nouă.</summary>
public sealed record RecalculateEstimatedTaxesCommand(Guid PfaRegistrationId, int TaxYear) : ICommand<Guid?>;

/// <summary>Ce vede fiecare dashboard (spec §11.1).</summary>
public sealed record GetEstimatedTaxesQuery(FiscalProfileScope Scope, Guid? PfaRegistrationId, int Year) : IQuery<EstimatedTaxesResponse>;

/// <summary>„Am deja pus deoparte: X lei” — spus de PFA în card.</summary>
public sealed record SetExistingReserveCommand(int Year, decimal? Amount) : ICommand<EstimatedTaxesResponse>;

/// <summary>Butonul „Recalculează” din admin/contabilitate: pune PFA-ul în coadă imediat.</summary>
public sealed record RequestEstimateRecalculationCommand(FiscalProfileScope Scope, Guid PfaRegistrationId, int Year) : ICommand<EstimatedTaxesResponse>;

/// <summary>O trecere a jobului: recalculează ce e expirat.</summary>
public sealed record ProcessFiscalEstimateQueueCommand : ICommand<int>;

/// <summary>
/// Marcarea ca expirate a rulărilor unui PFA, la orice schimbare care intră în calcul. Nu salvează:
/// se face în aceeași tranzacție cu schimbarea care a declanșat-o.
/// </summary>
public static class FiscalEstimateInvalidation
{
    /// <summary>Cât se așteaptă după ultima schimbare înainte de recalculare (debounce).</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(30);

    public static async Task MarkStaleAsync(
        IApplicationDbContext context,
        Guid pfaRegistrationId,
        int? taxYear,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<FiscalEstimateRun> runs = await context.FiscalEstimateRuns
            .Where(r => r.PfaRegistrationId == pfaRegistrationId && !r.Stale && (taxYear == null || r.TaxYear == taxYear))
            .ToListAsync(cancellationToken);

        foreach (FiscalEstimateRun run in runs)
        {
            run.Stale = true;
            run.StaleSinceUtc = nowUtc;
        }
    }
}

internal sealed class RecalculateEstimatedTaxesCommandHandler(
    IApplicationDbContext context,
    IFinancialSnapshotProvider snapshots,
    TaxYearParametersProvider parameters,
    ITaxEngine engine,
    IDateTimeProvider clock)
    : ICommandHandler<RecalculateEstimatedTaxesCommand, Guid?>
{
    public async Task<Result<Guid?>> Handle(RecalculateEstimatedTaxesCommand command, CancellationToken cancellationToken)
    {
        PfaTaxProfile? profile = await context.PfaTaxProfiles
            .SingleOrDefaultAsync(p => p.PfaRegistrationId == command.PfaRegistrationId && p.TaxYear == command.TaxYear, cancellationToken);

        // Motorul pornește doar pe un profil confirmat de PFA.
        if (profile is null || profile.Status != PfaTaxProfileStatus.Completed)
        {
            return Result.Success<Guid?>(null);
        }

        PfaRegistration pfa = await context.PfaRegistrations.SingleAsync(p => p.Id == command.PfaRegistrationId, cancellationToken);
        DateTime nowUtc = clock.UtcNow;
        var today = DateOnly.FromDateTime(FiscalProfileService.ToRomania(nowUtc));

        bool pendingCorrection = await context.PfaDataCorrectionRequests
            .AnyAsync(c => c.PfaRegistrationId == pfa.Id && c.State == DataCorrectionState.Open, cancellationToken);

        FinancialSnapshot snapshot = await snapshots.GetAsync(pfa, profile, today, cancellationToken);
        IncomeProjection projection = IncomeProjector.Project(snapshot);
        ProfileFlags flags = ProfileFlagsMapper.Map(FiscalProfileService.Deserialize(profile.AnswersJson), profile.TaxYear, pendingCorrection);

        var yearEnd = new DateOnly(profile.TaxYear, 12, 31);
        int weeksLeft = Math.Max(1, (int)Math.Ceiling(Math.Max(0, yearEnd.DayNumber - snapshot.AsOf.DayNumber) / 7m));
        var input = new TaxInput(projection, flags, snapshot.RecordedTaxPayments, profile.ExistingReserve, weeksLeft);

        TaxResult result = engine.Calculate(input, parameters.For(profile.TaxYear));

        await FiscalEstimateInvalidation.MarkStaleAsync(context, pfa.Id, profile.TaxYear, nowUtc, cancellationToken);
        FiscalEstimateRun run = EstimatedTaxesMapping.ToRun(pfa.Id, profile, snapshot, result, nowUtc);
        context.FiscalEstimateRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);

        return run.Id;
    }
}

internal sealed class ProcessFiscalEstimateQueueCommandHandler(
    IApplicationDbContext context,
    ICommandHandler<RecalculateEstimatedTaxesCommand, Guid?> recalculate,
    IDateTimeProvider clock,
    ILogger<ProcessFiscalEstimateQueueCommandHandler> logger)
    : ICommandHandler<ProcessFiscalEstimateQueueCommand, int>
{
    private const int BatchSize = 25;

    public async Task<Result<int>> Handle(ProcessFiscalEstimateQueueCommand command, CancellationToken cancellationToken)
    {
        DateTime nowUtc = clock.UtcNow;
        var today = DateOnly.FromDateTime(FiscalProfileService.ToRomania(nowUtc));
        int year = today.Year;
        DateTime debounced = nowUtc - FiscalEstimateInvalidation.Debounce;

        var candidates = await context.PfaTaxProfiles
            .AsNoTracking()
            .Where(p => p.Status == PfaTaxProfileStatus.Completed && p.TaxYear == year)
            .Select(p => new
            {
                p.PfaRegistrationId,
                Latest = context.FiscalEstimateRuns
                    .Where(r => r.PfaRegistrationId == p.PfaRegistrationId && r.TaxYear == year)
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .ThenBy(r => r.Stale)
                    .Select(r => new { r.Stale, r.StaleSinceUtc, r.AsOf })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        // Fără rulare, expirată de peste 30 de secunde, sau din altă zi (proiecția depinde de săptămânile rămase).
        var due = candidates
            .Where(c => c.Latest is null
                || c.Latest.Stale && (c.Latest.StaleSinceUtc ?? DateTime.MinValue) <= debounced
                || c.Latest.AsOf < today)
            .Select(c => c.PfaRegistrationId)
            .Take(BatchSize)
            .ToList();

        int done = 0;
        foreach (Guid pfaId in due)
        {
            try
            {
                await recalculate.Handle(new RecalculateEstimatedTaxesCommand(pfaId, year), cancellationToken);
                done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Calculul taxelor estimate a eșuat pentru PFA {PfaId}.", pfaId);
                await SaveErrorRunAsync(pfaId, year, today, nowUtc, cancellationToken);
            }
        }

        return done;
    }

    /// <summary>O rulare eșuată rămâne vizibilă ca „Nu am putut calcula”, nu ca cifrele vechi.</summary>
    private async Task SaveErrorRunAsync(Guid pfaId, int year, DateOnly today, DateTime nowUtc, CancellationToken cancellationToken)
    {
        try
        {
            PfaTaxProfile? profile = await context.PfaTaxProfiles
                .AsNoTracking()
                .SingleOrDefaultAsync(p => p.PfaRegistrationId == pfaId && p.TaxYear == year, cancellationToken);
            await FiscalEstimateInvalidation.MarkStaleAsync(context, pfaId, year, nowUtc, cancellationToken);
            var run = new FiscalEstimateRun
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = pfaId,
                TaxYear = year,
                ProfileRevision = profile?.Revision ?? 0,
                AsOf = today,
                Status = TaxStatuses.Error,
                CreatedAtUtc = nowUtc,
            };
            foreach (string component in new[] { TaxComponents.Cas, TaxComponents.Cass, TaxComponents.IncomeTax, TaxComponents.Reserve })
            {
                run.Calculations.Add(new FiscalCalculation { Id = Guid.NewGuid(), RunId = run.Id, Component = component, Status = TaxStatuses.Error });
            }

            context.FiscalEstimateRuns.Add(run);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Nu am putut salva rularea eșuată pentru PFA {PfaId}.", pfaId);
        }
    }
}

internal sealed class GetEstimatedTaxesQueryHandler(IApplicationDbContext context, FiscalProfileService profiles)
    : IQueryHandler<GetEstimatedTaxesQuery, EstimatedTaxesResponse>
{
    public async Task<Result<EstimatedTaxesResponse>> Handle(GetEstimatedTaxesQuery query, CancellationToken cancellationToken)
    {
        if (!FiscalProfileService.IsValidYear(query.Year))
        {
            return Result.Failure<EstimatedTaxesResponse>(FiscalProfileService.InvalidYear);
        }

        Result<PfaRegistration> pfa = await profiles.ResolveAsync(query.Scope, query.PfaRegistrationId, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<EstimatedTaxesResponse>(pfa.Error);
        }

        return await EstimatedTaxesMapping.BuildAsync(context, pfa.Value.Id, query.Year, query.Scope != FiscalProfileScope.Pfa, cancellationToken);
    }
}

internal sealed class SetExistingReserveCommandHandler(IApplicationDbContext context, FiscalProfileService profiles)
    : ICommandHandler<SetExistingReserveCommand, EstimatedTaxesResponse>
{
    public async Task<Result<EstimatedTaxesResponse>> Handle(SetExistingReserveCommand command, CancellationToken cancellationToken)
    {
        if (command.Amount is < 0 or > 10_000_000)
        {
            return Result.Failure<EstimatedTaxesResponse>(Error.Problem("EstimatedTaxes.InvalidReserve", "Suma pusă deoparte nu e validă."));
        }

        Result<PfaRegistration> pfa = await profiles.ResolveAsync(FiscalProfileScope.Pfa, null, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<EstimatedTaxesResponse>(pfa.Error);
        }

        PfaTaxProfile profile = await profiles.GetOrCreateAsync(pfa.Value, command.Year, cancellationToken);
        profile.ExistingReserve = command.Amount is decimal amount ? Math.Round(amount, 2) : null;
        await FiscalEstimateInvalidation.MarkStaleAsync(context, pfa.Value.Id, command.Year, profiles.UtcNow, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return await EstimatedTaxesMapping.BuildAsync(context, pfa.Value.Id, command.Year, false, cancellationToken);
    }
}

internal sealed class RequestEstimateRecalculationCommandHandler(IApplicationDbContext context, FiscalProfileService profiles)
    : ICommandHandler<RequestEstimateRecalculationCommand, EstimatedTaxesResponse>
{
    public async Task<Result<EstimatedTaxesResponse>> Handle(RequestEstimateRecalculationCommand command, CancellationToken cancellationToken)
    {
        Result<PfaRegistration> pfa = await profiles.ResolveAsync(command.Scope, command.PfaRegistrationId, cancellationToken);
        if (pfa.IsFailure)
        {
            return Result.Failure<EstimatedTaxesResponse>(pfa.Error);
        }

        // Expirată „de mult”, ca jobul s-o ia la următoarea trecere, fără debounce.
        await FiscalEstimateInvalidation.MarkStaleAsync(
            context, pfa.Value.Id, command.Year, profiles.UtcNow - FiscalEstimateInvalidation.Debounce, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return await EstimatedTaxesMapping.BuildAsync(
            context, pfa.Value.Id, command.Year, command.Scope != FiscalProfileScope.Pfa, cancellationToken);
    }
}

internal static class EstimatedTaxesMapping
{
    private static readonly string[] Order =
        [TaxComponents.Cas, TaxComponents.Cass, TaxComponents.IncomeTax, TaxComponents.PlatformTaxes];

    private sealed record ReserveExtras(
        decimal? Weekly,
        decimal? AnnualEstimated,
        IReadOnlyList<string> Missing,
        bool ExistingReserveAssumedZero,
        decimal RecordedTaxPayments);

    private sealed record Assumptions(IncomeProjection Projection, IReadOnlyList<string> Warnings);

    public static FiscalEstimateRun ToRun(Guid pfaId, PfaTaxProfile profile, FinancialSnapshot snapshot, TaxResult result, DateTime nowUtc)
    {
        var run = new FiscalEstimateRun
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfaId,
            TaxYear = profile.TaxYear,
            ProfileRevision = profile.Revision,
            RuleVersion = result.RuleVersion,
            FinancialSnapshotId = snapshot.Id,
            AsOf = snapshot.AsOf,
            SnapshotJson = JsonSerializer.Serialize(snapshot, FiscalProfileService.Json),
            AssumptionsJson = JsonSerializer.Serialize(new Assumptions(result.Projection, result.Warnings), FiscalProfileService.Json),
            MissingInputsJson = JsonSerializer.Serialize(
                result.Components.SelectMany(c => c.MissingInputs).Distinct().ToList(), FiscalProfileService.Json),
            Status = result.Reserve.Status,
            CreatedAtUtc = nowUtc,
        };

        foreach (ComponentResult component in result.Components)
        {
            run.Calculations.Add(new FiscalCalculation
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                Component = component.Component,
                Status = component.Status,
                Amount = component.Amount,
                ReasonCode = component.ReasonCode,
                MissingInputsJson = JsonSerializer.Serialize(component.MissingInputs, FiscalProfileService.Json),
                BreakdownJson = JsonSerializer.Serialize(component.Breakdown, FiscalProfileService.Json),
            });
        }

        ReserveResult reserve = result.Reserve;
        run.Calculations.Add(new FiscalCalculation
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            Component = TaxComponents.Reserve,
            Status = reserve.Status,
            Amount = reserve.Total,
            ReasonCode = reserve.ReasonCode,
            MissingInputsJson = JsonSerializer.Serialize(reserve.Missing, FiscalProfileService.Json),
            BreakdownJson = JsonSerializer.Serialize(
                new ReserveExtras(reserve.Weekly, reserve.AnnualEstimated, reserve.Missing, reserve.ExistingReserveAssumedZero, snapshot.RecordedTaxPayments),
                FiscalProfileService.Json),
        });

        return run;
    }

    public static async Task<EstimatedTaxesResponse> BuildAsync(
        IApplicationDbContext context,
        Guid pfaId,
        int year,
        bool staff,
        CancellationToken cancellationToken)
    {
        var profile = await context.PfaTaxProfiles
            .AsNoTracking()
            .Where(p => p.PfaRegistrationId == pfaId && p.TaxYear == year)
            .Select(p => new { p.Status, p.ExistingReserve })
            .SingleOrDefaultAsync(cancellationToken);

        PfaTaxProfileStatus status = profile?.Status ?? PfaTaxProfileStatus.NotStarted;
        if (status != PfaTaxProfileStatus.Completed)
        {
            return new EstimatedTaxesResponse(year, true, FiscalProfileService.StatusCode(status));
        }

        List<FiscalEstimateRun> runs = await context.FiscalEstimateRuns
            .AsNoTracking()
            .Include(r => r.Calculations)
            .Where(r => r.PfaRegistrationId == pfaId && r.TaxYear == year)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenBy(r => r.Stale)
            .Take(staff ? 10 : 1)
            .ToListAsync(cancellationToken);

        List<EstimatedTaxesRunSummary>? history = staff
            ? runs.Select(r => new EstimatedTaxesRunSummary(r.Id, r.CreatedAtUtc, r.Status, r.Stale, r.ProfileRevision, r.RuleVersion, r.AsOf)).ToList()
            : null;

        FiscalEstimateRun? latest = runs.FirstOrDefault();

        // Nicio rulare sau una expirată: „se calculează”, fără cifrele vechi.
        if (latest is null || latest.Stale)
        {
            return new EstimatedTaxesResponse(
                year,
                false,
                "COMPLETED",
                latest?.AsOf,
                TaxStatuses.Calculating,
                true,
                new EstimatedReserveResponse(TaxStatuses.Calculating, null, null, null, [], null, profile!.ExistingReserve, profile.ExistingReserve is null, 0),
                Order.Select(c => new EstimatedTaxComponentResponse(
                    c, c == TaxComponents.PlatformTaxes ? TaxStatuses.NotConfigured : TaxStatuses.Calculating, null, null, [], null)).ToList(),
                [],
                null,
                Runs: history);
        }

        FiscalCalculation? reserveRow = latest.Calculations.FirstOrDefault(c => c.Component == TaxComponents.Reserve);
        ReserveExtras extras = reserveRow is null
            ? new ReserveExtras(null, null, [], true, 0)
            : JsonSerializer.Deserialize<ReserveExtras>(reserveRow.BreakdownJson, FiscalProfileService.Json) ?? new ReserveExtras(null, null, [], true, 0);
        Assumptions? assumptions = JsonSerializer.Deserialize<Assumptions>(latest.AssumptionsJson, FiscalProfileService.Json);

        var components = Order
            .Select(name =>
            {
                FiscalCalculation? row = latest.Calculations.FirstOrDefault(c => c.Component == name);
                if (row is null)
                {
                    return new EstimatedTaxComponentResponse(
                        name, name == TaxComponents.PlatformTaxes ? TaxStatuses.NotConfigured : TaxStatuses.Error, null, null, [], null);
                }

                return new EstimatedTaxComponentResponse(
                    row.Component,
                    row.Status,
                    row.Amount,
                    row.ReasonCode,
                    JsonSerializer.Deserialize<List<string>>(row.MissingInputsJson, FiscalProfileService.Json) ?? [],
                    staff ? JsonSerializer.Deserialize<Dictionary<string, object?>>(row.BreakdownJson, FiscalProfileService.Json) : null);
            })
            .ToList();

        IncomeProjection? projection = assumptions?.Projection;

        return new EstimatedTaxesResponse(
            year,
            false,
            "COMPLETED",
            latest.AsOf,
            latest.Status,
            false,
            new EstimatedReserveResponse(
                reserveRow?.Status ?? TaxStatuses.Error,
                reserveRow?.Amount,
                extras.Weekly,
                extras.AnnualEstimated,
                extras.Missing,
                reserveRow?.ReasonCode,
                profile!.ExistingReserve,
                extras.ExistingReserveAssumedZero,
                extras.RecordedTaxPayments),
            components,
            assumptions?.Warnings ?? [],
            projection is null
                ? null
                : new EstimatedProjectionResponse(
                    projection.NetRealized, projection.NetAnnualEstimated, projection.WeeklyAverage, projection.WeeksUsed, projection.WeeksRemaining),
            staff ? latest.ProfileRevision : null,
            staff ? latest.RuleVersion : null,
            staff ? latest.FinancialSnapshotId : null,
            history);
    }
}
