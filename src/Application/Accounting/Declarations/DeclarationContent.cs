using Application.Abstractions.Anaf;
using Application.Abstractions.Data;
using Application.Accounting.Months;
using Application.Accounting.Tax;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Declarations;

/// <summary>Calculul unei declarații din datele de acum, gata de scris pe o versiune.</summary>
internal sealed record DeclarationDraft(PfaTaxInput Input, DeclarationCalculation Calculation);

/// <summary>
/// Conținutul unei versiuni (spec contabilitate B3–B5): calculul, snapshot-ul, liniile și XML-ul.
/// Același drum pentru generare, regenerare (pe loc) și rectificativă (versiune nouă).
/// </summary>
internal static class DeclarationContent
{
    /// <summary>
    /// Recalculează un tip de declarație pentru un PFA și o lună. Eșuează (409) dacă luna nu mai e
    /// gata sau declarația nu mai are bază; versiunea rămâne atunci neatinsă.
    /// </summary>
    public static async Task<Result<DeclarationDraft>> CalculateAsync(
        IApplicationDbContext db,
        Guid pfaId,
        string period,
        DeclarationType type,
        TaxEngineSettings settings,
        CancellationToken cancellationToken)
    {
        var registration = await db.PfaRegistrations
            .AsNoTracking()
            .Where(p => p.Id == pfaId)
            .Select(p => new { p.Id, p.LegalName, p.FullName, p.Cui })
            .SingleOrDefaultAsync(cancellationToken);
        if (registration is null)
        {
            return Result.Failure<DeclarationDraft>(AccountingErrors.PfaNotFound);
        }

        var pfa = new ScopePfa(registration.Id, registration.LegalName ?? registration.FullName ?? string.Empty, registration.Cui ?? string.Empty);
        MonthData data = await MonthData.LoadAsync(db, period, [pfaId], cancellationToken);
        PreCheckResult check = PreCheck.Evaluate(pfa, data, settings);
        await PreCheck.SaveAsync(db, pfaId, period, check, cancellationToken);
        if (!check.IsReady)
        {
            return Result.Failure<DeclarationDraft>(DeclarationErrors.MonthNotReady(check.Reasons.Count > 0 ? check.Reasons[0] : period));
        }

        PfaTaxInput input = data.TaxInput(pfaId, settings);
        TaxResult result = MonthlyTaxEngine.Calculate(input);
        if (result.IsBlocked)
        {
            return Result.Failure<DeclarationDraft>(DeclarationErrors.MonthNotReady(result.BlockingReasons[0]));
        }

        DeclarationCalculation calculation = result.Declarations[type];
        return calculation.Applicable
            ? new DeclarationDraft(input, calculation)
            : Result.Failure<DeclarationDraft>(DeclarationErrors.NoBase(type, period));
    }

    /// <summary>
    /// Scrie calculul pe versiune: suma, snapshot-ul, schema perioadei, liniile și XML-ul. Liniile
    /// existente nu se șterg, se marchează înlocuite; PDF-ul și rezultatul validării vechi se golesc.
    /// </summary>
    public static async Task ApplyAsync(
        IApplicationDbContext db,
        DeclarationFiles files,
        Declaration declaration,
        DeclarationVersion version,
        DeclarationDraft draft,
        AnafTaxpayer? taxpayer,
        IEnumerable<AnafDeclarationSchema> schemas,
        string? xmlBlocker,
        CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        List<DeclarationLine> previous = await db.DeclarationLines
            .Where(line => line.DeclarationVersionId == version.Id && line.SupersededAtUtc == null)
            .ToListAsync(cancellationToken);
        previous.ForEach(line => line.SupersededAtUtc = now);

        AnafDeclarationSchema? schema = DeclarationFiles.PickSchema(schemas, declaration.Type, declaration.Period);
        var snapshot = new DeclarationSnapshot(draft.Input, draft.Calculation, taxpayer is null ? null : taxpayer with { Iban = null }, now);
        version.Amount = draft.Calculation.Total;
        version.SnapshotJson = AccountingJson.Serialize(snapshot);
        version.SchemaId = schema?.Id;
        version.XmlDocumentId = null;
        version.PdfDocumentId = null;
        version.ValidationResultJson = null;

        db.DeclarationLines.AddRange(draft.Calculation.Lines.Select(line => new DeclarationLine
        {
            Id = Guid.NewGuid(),
            DeclarationVersionId = version.Id,
            SourceDocumentId = line.SourceDocumentId,
            RuleCode = line.RuleCode,
            AmountInCurrency = line.AmountInCurrency,
            Base = line.Base,
            Rate = line.Rate,
            Value = line.Value,
            Currency = line.Currency,
            ExchangeRate = line.ExchangeRate,
            Explanation = line.Explanation,
            SupplierName = line.SupplierName,
            SupplierCountry = line.SupplierCountry,
            SupplierVatId = line.SupplierVatId,
            OperationType = line.OperationType,
            Treaty = line.Treaty,
            ResidenceCertValidFrom = line.ResidenceCertValidFrom,
            ResidenceCertValidTo = line.ResidenceCertValidTo,
        }));

        if (taxpayer is not null && xmlBlocker is null)
        {
            await files.WriteXmlAsync(declaration, version, taxpayer, snapshot, schema, cancellationToken);
        }
    }

    /// <summary>
    /// De ce o versiune nu poate avea XML, deși calculul e în regulă: corecția D100 prin D710,
    /// fără generator (procedura e DE CONFIRMAT).
    /// </summary>
    public static string? XmlBlocker(DeclarationType type, DeclarationVersionKind kind, D100CorrectionProcedure procedure) =>
        type == DeclarationType.D100 && kind == DeclarationVersionKind.Rectificative && procedure == D100CorrectionProcedure.D710
            ? "Corecția D100 se depune prin D710 (Accounting:D100CorrectionProcedure, DE CONFIRMAT); generatorul XML D710 nu există încă, lipsește schema oficială."
            : null;
}
