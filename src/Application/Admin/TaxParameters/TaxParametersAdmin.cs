using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.FiscalEstimates;
using Application.FiscalProfiles;
using Domain.AppSettings;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Admin.TaxParameters;

/// <summary>Plafoanele și cotele unui an, în forma formularului din admin.</summary>
/// <param name="CasRate">Cota ca fracție (0,25 = 25%). Formularul o arată în procente.</param>
public sealed record TaxParametersValues(
    decimal MinWageReference,
    decimal CassMinThreshold,
    decimal CasThreshold12,
    decimal CasThreshold24,
    decimal CassMaxBase,
    decimal CasRate,
    decimal CassRate,
    decimal IncomeTaxRate,
    decimal CarriedLossOffsetLimit);

/// <param name="Current">Ce se folosește acum în calcul. Null când anul nu are parametri deloc.</param>
/// <param name="Defaults">Valorile din fișierul anului, pentru comparație și resetare. Null fără fișier.</param>
public sealed record TaxParametersResponse(
    int TaxYear,
    TaxParametersValues? Current,
    TaxParametersValues? Defaults,
    bool IsOverridden,
    string? RuleVersion,
    string? Source,
    DateTime? UpdatedAtUtc,
    IReadOnlyList<int> AvailableYears);

public static class TaxParametersErrors
{
    public static Error Invalid(string message) => Error.Problem("TaxParameters.Invalid", message);
}

/// <summary>Parametrii fiscali ai unui an, pentru cardul din „Privire de ansamblu".</summary>
public sealed record GetTaxParametersQuery(int? TaxYear) : IQuery<TaxParametersResponse>;

/// <summary>
/// Salvează plafoanele unui an peste valorile din fișier. Versiunea regulii se schimbă odată cu
/// ele, iar coada de estimări recalculează singură toate PFA-urile pe care le-a calculat cu
/// versiunea veche — „Cât să pui deoparte" se actualizează fără alt pas.
/// </summary>
public sealed record UpdateTaxParametersCommand(int TaxYear, TaxParametersValues Values) : ICommand<TaxParametersResponse>;

/// <summary>Șterge valorile din admin: anul revine la fișier (și estimările, la versiunea lui).</summary>
public sealed record ResetTaxParametersCommand(int TaxYear) : ICommand<TaxParametersResponse>;

internal static class TaxParametersAdmin
{
    public static TaxParametersValues ToValues(TaxYearParameters p) => new(
        p.MinWageReference,
        p.CassMinThreshold,
        p.CasThreshold12,
        p.CasThreshold24,
        p.CassMaxBase,
        p.CasRate,
        p.CassRate,
        p.IncomeTaxRate,
        p.CarriedLossOffsetLimit);

    public static async Task<TaxParametersResponse> ResponseAsync(
        IApplicationDbContext context,
        TaxYearParametersProvider provider,
        int year,
        CancellationToken cancellationToken)
    {
        TaxYearParameters? current = provider.For(year);
        TaxYearParameters? defaults = provider.DefaultFor(year);

        DateTime? updatedAt = await context.AppSettings
            .AsNoTracking()
            .Where(s => s.Key == TaxYearParametersProvider.SettingKey(year))
            .Select(s => (DateTime?)s.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        int thisYear = FiscalProfileService.ToRomania(DateTime.UtcNow).Year;
        List<string> overrideKeys = await context.AppSettings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith(TaxYearParametersProvider.SettingKeyPrefix))
            .Select(s => s.Key)
            .ToListAsync(cancellationToken);

        // Anul curent și următorul apar mereu: plafoanele anului viitor se publică din toamnă.
        var years = new SortedSet<int> { thisYear, thisYear + 1, year };
        foreach (string key in overrideKeys)
        {
            if (int.TryParse(key[TaxYearParametersProvider.SettingKeyPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            {
                years.Add(y);
            }
        }

        for (int y = thisYear - 1; y <= thisYear + 1; y++)
        {
            if (provider.DefaultFor(y) is not null)
            {
                years.Add(y);
            }
        }

        return new TaxParametersResponse(
            year,
            current is null ? null : ToValues(current),
            defaults is null ? null : ToValues(defaults),
            provider.IsOverridden(year),
            current?.RuleVersion,
            current?.Source,
            updatedAt,
            years.ToList());
    }

    /// <summary>Verificări de bun-simț: o cifră greșită aici schimbă taxele tuturor.</summary>
    public static string? Validate(TaxParametersValues v)
    {
        if (v.MinWageReference is < 1_000 or > 50_000)
        {
            return "Salariul minim trebuie să fie între 1.000 și 50.000 lei.";
        }

        if (new[] { v.CassMinThreshold, v.CasThreshold12, v.CasThreshold24, v.CassMaxBase }.Any(t => t <= 0))
        {
            return "Plafoanele trebuie să fie mai mari decât zero.";
        }

        if (!(v.CassMinThreshold < v.CasThreshold12 && v.CasThreshold12 < v.CasThreshold24 && v.CasThreshold24 < v.CassMaxBase))
        {
            return "Plafoanele trebuie să crească: CASS minim < CAS 12 salarii < CAS 24 salarii < CASS maxim.";
        }

        if (new[] { v.CasRate, v.CassRate, v.IncomeTaxRate }.Any(r => r is <= 0 or >= 1))
        {
            return "Cotele trebuie să fie între 0% și 100%.";
        }

        if (v.CarriedLossOffsetLimit is < 0 or > 1)
        {
            return "Limita pentru pierderile reportate trebuie să fie între 0% și 100%.";
        }

        return null;
    }
}

internal sealed class GetTaxParametersQueryHandler(
    IApplicationDbContext context,
    TaxYearParametersProvider provider)
    : IQueryHandler<GetTaxParametersQuery, TaxParametersResponse>
{
    public async Task<Result<TaxParametersResponse>> Handle(GetTaxParametersQuery query, CancellationToken cancellationToken)
    {
        int year = query.TaxYear ?? FiscalProfileService.ToRomania(DateTime.UtcNow).Year;
        return await TaxParametersAdmin.ResponseAsync(context, provider, year, cancellationToken);
    }
}

internal sealed class UpdateTaxParametersCommandHandler(
    IApplicationDbContext context,
    TaxYearParametersProvider provider,
    IUserContext userContext)
    : ICommandHandler<UpdateTaxParametersCommand, TaxParametersResponse>
{
    public async Task<Result<TaxParametersResponse>> Handle(UpdateTaxParametersCommand command, CancellationToken cancellationToken)
    {
        if (command.TaxYear is < 2020 or > 2100)
        {
            return Result.Failure<TaxParametersResponse>(TaxParametersErrors.Invalid("Anul fiscal nu e valid."));
        }

        string? problem = TaxParametersAdmin.Validate(command.Values);
        if (problem is not null)
        {
            return Result.Failure<TaxParametersResponse>(TaxParametersErrors.Invalid(problem));
        }

        DateTime now = DateTime.UtcNow;
        TaxParametersValues v = command.Values;
        TaxYearParameters? defaults = provider.DefaultFor(command.TaxYear);

        var parameters = new TaxYearParameters(
            command.TaxYear,
            // Versiune nouă la fiecare salvare: după ea recunoaște coada estimările de refăcut.
            $"{command.TaxYear}.admin.{now:yyyyMMddHHmmss}",
            v.MinWageReference,
            v.CassMinThreshold,
            v.CasThreshold12,
            v.CasThreshold24,
            v.CassMaxBase,
            v.CasRate,
            v.CassRate,
            v.IncomeTaxRate,
            defaults is null ? "Setat din admin" : $"Modificat din admin (implicit: {defaults.Source})",
            v.CarriedLossOffsetLimit);

        string key = TaxYearParametersProvider.SettingKey(command.TaxYear);
        AppSetting? setting = await context.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (setting is null)
        {
            setting = new AppSetting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Description = $"Plafoanele și cotele fiscale pentru {command.TaxYear}, setate din admin.",
                CreatedAtUtc = now,
            };
            context.AppSettings.Add(setting);
        }

        setting.ValueJson = TaxYearParametersProvider.Serialize(parameters);
        setting.UpdatedAtUtc = now;
        setting.UpdatedByUserId = userContext.UserId;

        await context.SaveChangesAsync(cancellationToken);

        // Pe instanța asta, imediat; pe celelalte, la următoarea sincronizare (cel mult un minut).
        provider.SetOverride(parameters);

        return await TaxParametersAdmin.ResponseAsync(context, provider, command.TaxYear, cancellationToken);
    }
}

internal sealed class ResetTaxParametersCommandHandler(
    IApplicationDbContext context,
    TaxYearParametersProvider provider)
    : ICommandHandler<ResetTaxParametersCommand, TaxParametersResponse>
{
    public async Task<Result<TaxParametersResponse>> Handle(ResetTaxParametersCommand command, CancellationToken cancellationToken)
    {
        string key = TaxYearParametersProvider.SettingKey(command.TaxYear);
        AppSetting? setting = await context.AppSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (setting is not null)
        {
            context.AppSettings.Remove(setting);
            await context.SaveChangesAsync(cancellationToken);
        }

        provider.RemoveOverride(command.TaxYear);

        return await TaxParametersAdmin.ResponseAsync(context, provider, command.TaxYear, cancellationToken);
    }
}
