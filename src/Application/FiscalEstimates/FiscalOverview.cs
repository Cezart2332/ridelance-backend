using System.Text.Json;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.FiscalProfiles;
using Domain.FiscalEstimates;
using Domain.FiscalProfiles;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.FiscalEstimates;

/// <summary>
/// Un rând din „Clienți PFA”, coloanele fiscale ale anului: profilul, veniturile până la data
/// ultimei rulări și taxele estimate din ea. <c>null</c> = încă necalculat (profil necompletat sau
/// rulare lipsă), niciodată 0 inventat.
/// </summary>
public sealed record FiscalOverviewRow(
    Guid PfaId,
    string ProfileStatus,
    string? ProfileLabel,
    DateOnly? AsOf,
    bool Stale,
    decimal? GrossIncome,
    decimal? Expenses,
    decimal? NetIncome,
    decimal? Cas,
    decimal? Cass,
    decimal? IncomeTax,
    decimal? TotalTaxes);

/// <summary>Plafoanele anului, pentru barele și alertele din tabel; <c>null</c> când anul nu are parametri.</summary>
public sealed record FiscalThresholds(decimal Cas12, decimal Cas24, decimal CassMin, decimal CassMax, decimal VatArt310);

public sealed record FiscalOverviewDto(int Year, FiscalThresholds? Thresholds, IReadOnlyList<FiscalOverviewRow> Rows);

/// <summary>
/// Plafonul de scutire TVA (art. 310 Cod fiscal), 395.000 lei de la 1 septembrie 2025. Nu ține de
/// anul fiscal al contribuțiilor, deci nu stă în parametrii anului.
/// </summary>
public static class VatThresholds
{
    public const decimal Art310 = 395_000m;
}

/// <summary><c>GET /accounting/fiscal-overview?year=</c> — un rând pe PFA, pentru coloanele fiscale ale tabelului.</summary>
public sealed record ListFiscalOverviewQuery(int Year) : IQuery<FiscalOverviewDto>;

internal sealed class ListFiscalOverviewQueryHandler(IApplicationDbContext context, TaxYearParametersProvider parameters, IUserContext userContext)
    : IQueryHandler<ListFiscalOverviewQuery, FiscalOverviewDto>
{
    public async Task<Result<FiscalOverviewDto>> Handle(ListFiscalOverviewQuery query, CancellationToken cancellationToken)
    {
        Guid me = userContext.UserId;
        bool accountant = await context.Users.AnyAsync(u => u.Id == me && u.Role == UserRole.Contabil, cancellationToken);
        // Același portofoliu ca lista Clienți PFA: contabilul își vede numai clienții alocați.
        List<Guid> portfolio = await context.PfaRegistrations.AsNoTracking()
            .Where(p => p.User.DeletedAtUtc == null && (!accountant || p.AssignedContabilId == me))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        var profiles = await context.PfaTaxProfiles.AsNoTracking()
            .Where(p => p.TaxYear == query.Year && portfolio.Contains(p.PfaRegistrationId))
            .Select(p => new { p.PfaRegistrationId, p.Status, p.AnswersJson })
            .ToListAsync(cancellationToken);

        // Ultima rulare a fiecărui PFA pe anul cerut.
        List<FiscalEstimateRun> runs = await context.FiscalEstimateRuns.AsNoTracking()
            .Include(r => r.Calculations)
            .Where(r => r.TaxYear == query.Year && portfolio.Contains(r.PfaRegistrationId) &&
                        r.CreatedAtUtc == context.FiscalEstimateRuns
                            .Where(other => other.PfaRegistrationId == r.PfaRegistrationId && other.TaxYear == query.Year)
                            .Max(other => other.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        var latest = runs
            .GroupBy(r => r.PfaRegistrationId)
            .ToDictionary(g => g.Key, g => g.First());

        var rows = new List<FiscalOverviewRow>();
        foreach (var profile in profiles)
        {
            latest.TryGetValue(profile.PfaRegistrationId, out FiscalEstimateRun? run);
            rows.Add(Row(profile.PfaRegistrationId, profile.Status, profile.AnswersJson, run));
        }

        foreach (FiscalEstimateRun run in latest.Values.Where(r => profiles.TrueForAll(p => p.PfaRegistrationId != r.PfaRegistrationId)))
        {
            rows.Add(Row(run.PfaRegistrationId, PfaTaxProfileStatus.NotStarted, "{}", run));
        }

        FiscalThresholds? thresholds = parameters.For(query.Year) is { } year
            ? new FiscalThresholds(year.CasThreshold12, year.CasThreshold24, year.CassMinThreshold, year.CassMaxBase, VatThresholds.Art310)
            : null;
        return new FiscalOverviewDto(query.Year, thresholds, rows);
    }

    private static FiscalOverviewRow Row(Guid pfaId, PfaTaxProfileStatus status, string answersJson, FiscalEstimateRun? run)
    {
        string profileStatus = status switch
        {
            PfaTaxProfileStatus.Completed => "COMPLETED",
            PfaTaxProfileStatus.Draft => "DRAFT",
            _ => "NOT_STARTED",
        };
        string? label = status == PfaTaxProfileStatus.Completed ? FiscalProfileLabels.Of(answersJson) : null;
        if (run is null)
        {
            return new FiscalOverviewRow(pfaId, profileStatus, label, null, false, null, null, null, null, null, null, null);
        }

        FinancialSnapshot? snapshot = ReadSnapshot(run.SnapshotJson);
        decimal? gross = snapshot?.GrossIncomeYtd;
        decimal? expenses = snapshot?.DeductibleExpensesYtd;
        decimal? cas = Amount(run, TaxComponents.Cas);
        decimal? cass = Amount(run, TaxComponents.Cass);
        decimal? tax = Amount(run, TaxComponents.IncomeTax);
        decimal? total = cas is null && cass is null && tax is null ? null : (cas ?? 0) + (cass ?? 0) + (tax ?? 0);
        return new FiscalOverviewRow(
            pfaId, profileStatus, label, run.AsOf, run.Stale,
            gross, expenses, gross is null ? null : Math.Max(0, gross.Value - (expenses ?? 0)),
            cas, cass, tax, total);
    }

    private static decimal? Amount(FiscalEstimateRun run, string component) =>
        run.Calculations.FirstOrDefault(c => c.Component == component)?.Amount;

    private static FinancialSnapshot? ReadSnapshot(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<FinancialSnapshot>(json, FiscalProfileService.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Eticheta scurtă a profilului fiscal, din răspunsurile PFA-ului (situațiile care schimbă taxele).</summary>
public static class FiscalProfileLabels
{
    public static string Of(string answersJson)
    {
        Dictionary<string, JsonElement> answers;
        try
        {
            answers = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(answersJson) ?? [];
        }
        catch (JsonException)
        {
            return "Standard";
        }

        string? Answer(string key) =>
            answers.TryGetValue(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        if (Answer("pensioner") == "yes")
        {
            return "Pensionar";
        }

        if (Answer("ownPensionSystem") == "yes")
        {
            return "Sistem propriu";
        }

        if (Answer("employment") is "full" or "part")
        {
            return Answer("salaryAboveCassMin") == "yes" ? "Salariat ≥ 6 salarii" : "Salariat < 6 salarii";
        }

        return Answer("student") == "yes" ? "Student" : "Standard";
    }
}
