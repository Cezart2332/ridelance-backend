using System.Globalization;
using Application.Abstractions.Data;
using Application.Accounting.Contracts;
using Application.Accounting.Tax;
using Domain.Accounting;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;

namespace Application.Accounting.Months;

/// <summary>Un PFA din luna fiscală.</summary>
public sealed record ScopePfa(Guid Id, string Name, string Cui);

/// <summary>Un document al lunii, cu extracția curentă.</summary>
internal sealed record MonthDocument(PlatformDocument Document, string FileName, DocumentExtraction? Extraction);

/// <summary>
/// Ce PFA-uri intră într-o lună fiscală. Cu o colaborare contabilă înregistrată, contează intervalul
/// ei; fără, orice PFA cu onboardingul încheiat până la sfârșitul lunii și cont activ.
/// </summary>
internal static class AccountingScope
{
    public static async Task<List<ScopePfa>> InPeriodAsync(IApplicationDbContext db, string period, CancellationToken cancellationToken, Guid? only = null)
    {
        (DateOnly start, DateOnly end) = Bounds(period);
        var endUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        List<PfaAccountingEngagement> engagements = await db.PfaAccountingEngagements
            .AsNoTracking()
            .Where(e => only == null || e.PfaRegistrationId == only)
            .ToListAsync(cancellationToken);
        List<Guid> engaged = [.. engagements.Select(e => e.PfaRegistrationId).Distinct()];
        List<Guid> covering = [.. engagements
            .Where(e => e.StartDate <= end && (e.EndDate == null || e.EndDate >= start))
            .Select(e => e.PfaRegistrationId)
            .Distinct()];

        var rows = await db.PfaRegistrations
            .AsNoTracking()
            .Where(p => only == null || p.Id == only)
            .Where(p => covering.Contains(p.Id) ||
                        !engaged.Contains(p.Id) &&
                        p.OnboardingCompletedAtUtc != null &&
                        p.OnboardingCompletedAtUtc < endUtc &&
                        p.User.DeletedAtUtc == null)
            .Select(p => new { p.Id, p.LegalName, p.FullName, p.Cui, p.User.FirstName, p.User.LastName })
            .ToListAsync(cancellationToken);

        return [.. rows
            .Select(p => new ScopePfa(p.Id, p.LegalName ?? p.FullName ?? $"{p.FirstName} {p.LastName}".Trim(), p.Cui ?? string.Empty))
            .OrderBy(p => p.Name, StringComparer.Create(CultureInfo.GetCultureInfo("ro-RO"), ignoreCase: true))];
    }

    public static (DateOnly Start, DateOnly End) Bounds(string period)
    {
        var start = DateOnly.ParseExact($"{period}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (start, start.AddMonths(1).AddDays(-1));
    }
}

/// <summary>
/// Datele unei luni pentru un grup de PFA-uri, încărcate dintr-o dată: documentele, setările,
/// regulile și cursurile. Pre-check-ul, calculul și privirea de ansamblu lucrează pe ele, fără
/// interogări pe fiecare PFA.
/// </summary>
internal sealed class MonthData
{
    private readonly ILookup<Guid, MonthDocument> _documents;
    private readonly ILookup<Guid, PfaAccountingSetting> _settings;
    private readonly ILookup<Guid, Platform> _onboardingPlatforms;

    private MonthData(
        string period,
        ILookup<Guid, MonthDocument> documents,
        ILookup<Guid, PfaAccountingSetting> settings,
        ILookup<Guid, Platform> onboardingPlatforms,
        List<SupplierTaxProfile> suppliers,
        List<VatRate> vatRates,
        List<D100Rule> d100Rules,
        List<ExchangeRate> exchangeRates)
    {
        Period = period;
        (Start, End) = AccountingScope.Bounds(period);
        _documents = documents;
        _settings = settings;
        _onboardingPlatforms = onboardingPlatforms;
        Suppliers = suppliers;
        VatRates = vatRates;
        D100Rules = d100Rules;
        ExchangeRates = exchangeRates;
    }

    public string Period { get; }
    public DateOnly Start { get; }
    public DateOnly End { get; }
    public List<SupplierTaxProfile> Suppliers { get; }
    public List<VatRate> VatRates { get; }
    public List<D100Rule> D100Rules { get; }
    public List<ExchangeRate> ExchangeRates { get; }

    public IEnumerable<MonthDocument> DocumentsOf(Guid pfaId) => _documents[pfaId];

    public static async Task<MonthData> LoadAsync(IApplicationDbContext db, string period, IReadOnlyCollection<Guid> pfaIds, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. pfaIds];
        (DateOnly start, DateOnly end) = AccountingScope.Bounds(period);

        var documents = await db.PlatformDocuments
            .AsNoTracking()
            .Where(d => ids.Contains(d.PfaRegistrationId) && d.Period == period)
            .Select(d => new
            {
                Document = d,
                FileName = d.SourceDocument.OriginalFileName,
                Extraction = d.Extractions.FirstOrDefault(e => e.IsCurrent),
            })
            .ToListAsync(cancellationToken);

        List<PfaAccountingSetting> settings = await db.PfaAccountingSettings
            .AsNoTracking()
            .Where(s => ids.Contains(s.PfaRegistrationId))
            .ToListAsync(cancellationToken);

        var accounts = await db.PfaPlatformAccounts
            .AsNoTracking()
            .Where(a => ids.Contains(a.PfaRegistrationId) && a.IsSelectedByUser)
            .Select(a => new { a.PfaRegistrationId, a.Provider })
            .ToListAsync(cancellationToken);

        // Cursurile: două săptămâni înainte de lună acoperă weekendurile și sărbătorile.
        DateOnly ratesFrom = start.AddDays(-14);
        List<ExchangeRate> rates = await db.ExchangeRates.AsNoTracking()
            .Where(r => r.Date >= ratesFrom && r.Date <= end)
            .ToListAsync(cancellationToken);

        return new MonthData(
            period,
            documents.Select(d => new MonthDocument(d.Document, d.FileName, d.Extraction)).ToLookup(d => d.Document.PfaRegistrationId),
            settings.ToLookup(s => s.PfaRegistrationId),
            accounts.ToLookup(a => a.PfaRegistrationId, a => a.Provider == PfaPlatformProvider.Bolt ? Platform.Bolt : Platform.Uber),
            await db.SupplierTaxProfiles.AsNoTracking().ToListAsync(cancellationToken),
            await db.VatRates.AsNoTracking().ToListAsync(cancellationToken),
            await db.D100Rules.AsNoTracking().ToListAsync(cancellationToken),
            rates);
    }

    /// <summary>
    /// Platformele așteptate la sfârșitul lunii: setarea contabilă, altfel platformele alese la
    /// onboarding. <c>null</c> dacă nu se știu deloc.
    /// </summary>
    public IReadOnlyList<Platform>? PlatformsOf(Guid pfaId)
    {
        PfaAccountingSetting? setting = SettingAt(pfaId, PfaAccountingSettingKeys.Platforms, End);
        if (setting is not null)
        {
            return AccountingJson.Deserialize<List<Platform>>(setting.ValueJson, []);
        }

        List<Platform> onboarding = [.. _onboardingPlatforms[pfaId].Distinct()];
        return onboarding.Count > 0 ? onboarding : null;
    }

    public IReadOnlyList<Art317Period> Art317Of(Guid pfaId) =>
        [.. _settings[pfaId]
            .Where(s => s.Key == PfaAccountingSettingKeys.Art317)
            .OrderBy(s => s.ValidFrom)
            .Select(s => new Art317Period(AccountingJson.Deserialize(s.ValueJson, false), s.ValidFrom))];

    public bool Art317ActiveAtEnd(Guid pfaId) =>
        SettingAt(pfaId, PfaAccountingSettingKeys.Art317, End) is { } setting && AccountingJson.Deserialize(setting.ValueJson, false);

    /// <summary>Intrarea motorului fiscal: doar documentele confirmate (sau blocate după confirmare).</summary>
    public PfaTaxInput TaxInput(Guid pfaId, TaxEngineSettings settings)
    {
        List<MonthDocument> confirmed = [.. DocumentsOf(pfaId)
            .Where(d => d.Extraction is not null && d.Document.Status is PlatformDocumentStatus.Confirmed or PlatformDocumentStatus.Locked)];

        return new PfaTaxInput(
            Period,
            [.. confirmed
                .Where(d => d.Document.DocumentType == PlatformDocumentType.CommissionInvoice)
                .Select(d => new TaxInvoice(
                    d.Document.Id,
                    Label(d),
                    d.Extraction!.SupplierVatId,
                    d.Extraction.InvoiceNumber,
                    d.Extraction.InvoiceDate,
                    d.Extraction.PeriodTo,
                    d.Extraction.Currency,
                    d.Extraction.CommissionAmount))],
            [.. confirmed
                .Where(d => d.Document.DocumentType == PlatformDocumentType.PlatformReport)
                .Select(d => new TaxReport(d.Document.Id, d.Extraction!.Currency, d.Extraction.Amount, d.Extraction.PeriodTo))],
            Suppliers,
            VatRates,
            D100Rules,
            ExchangeRates,
            Art317Of(pfaId),
            settings);
    }

    /// <summary>„Factura Bolt EE-BOLT-2026-08-1000”, „Raportul Uber”.</summary>
    public static string Label(MonthDocument document)
    {
        string platform = document.Document.Platform switch
        {
            Platform.Bolt => "Bolt",
            Platform.Uber => "Uber",
            _ => string.Empty,
        };
        return document.Document.DocumentType switch
        {
            PlatformDocumentType.CommissionInvoice => $"Factura {platform} {document.Extraction?.InvoiceNumber}".Trim(),
            PlatformDocumentType.PlatformReport => $"Raportul {platform}".Trim(),
            _ => document.FileName,
        };
    }

    private PfaAccountingSetting? SettingAt(Guid pfaId, string key, DateOnly date) =>
        _settings[pfaId].Where(s => s.Key == key && s.ValidFrom <= date).MaxBy(s => s.ValidFrom);
}
