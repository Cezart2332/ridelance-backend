using Application.Abstractions.Data;
using Application.Accounting.Contracts;
using Application.Accounting.Tax;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace Application.Accounting.Months;

/// <summary>O intrare din istoricul de status al unei versiuni (coloana <c>status_history_json</c>).</summary>
internal sealed record StatusHistoryRecord(DeclarationStatus? From, DeclarationStatus To, DateTime At, Guid? ByUserId, string? Note);

/// <summary>
/// Generarea declarațiilor unei luni pentru un PFA gata (spec contabilitate B3): calculul, apoi
/// câte o <see cref="Declaration"/> cu versiunea 1 în <c>GENERATED</c> pentru fiecare tip aplicabil,
/// cu snapshot-ul intrărilor și al calculului. XML-ul se generează în B4.
/// </summary>
internal static class DeclarationGeneration
{
    /// <summary>Întoarce dacă a reușit și mesajul pentru rezumatul jobului.</summary>
    public static async Task<(bool Ok, string Message)> GenerateAsync(
        IApplicationDbContext db,
        ScopePfa pfa,
        MonthData data,
        TaxEngineSettings settings,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        // Idempotență: a doua rulare nu creează versiuni duplicate.
        if (await db.Declarations.AnyAsync(d => d.PfaRegistrationId == pfa.Id && d.Period == data.Period, cancellationToken))
        {
            return (true, "Declarațiile existau deja; nimic de generat.");
        }

        PreCheckResult check = PreCheck.Evaluate(pfa, data, settings);
        await PreCheck.SaveAsync(db, pfa.Id, data.Period, check, cancellationToken);
        if (!check.IsReady)
        {
            return (false, $"Nu mai e gata: {(check.Reasons.Count > 0 ? check.Reasons[0] : string.Empty)}");
        }

        PfaTaxInput input = data.TaxInput(pfa.Id, settings);
        TaxResult result = MonthlyTaxEngine.Calculate(input);
        if (result.IsBlocked)
        {
            return (false, result.BlockingReasons[0]);
        }

        List<AnafDeclarationSchema> schemas = await db.AnafDeclarationSchemas.AsNoTracking().ToListAsync(cancellationToken);
        var generated = new List<string>();
        foreach (DeclarationCalculation calculation in result.Declarations.Values.Where(c => c.Applicable))
        {
            var declaration = new Declaration { Id = Guid.NewGuid(), PfaRegistrationId = pfa.Id, Period = data.Period, Type = calculation.Type };
            var version = new DeclarationVersion
            {
                Id = Guid.NewGuid(),
                DeclarationId = declaration.Id,
                VersionNo = 1,
                Kind = DeclarationVersionKind.Initial,
                Status = DeclarationStatus.Generated,
                SchemaId = schemas.FirstOrDefault(s =>
                    s.DeclarationType == calculation.Type && s.ValidFrom <= data.End && (s.ValidTo == null || s.ValidTo >= data.End))?.Id,
                Amount = calculation.Total,
                SnapshotJson = AccountingJson.Serialize(new { input, calculation, calculatedAtUtc = DateTime.UtcNow }),
                StatusHistoryJson = AccountingJson.Serialize(new[] { new StatusHistoryRecord(null, DeclarationStatus.Generated, DateTime.UtcNow, userId, null) }),
                CreatedByUserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
            };

            db.Declarations.Add(declaration);
            db.DeclarationVersions.Add(version);
            db.DeclarationLines.AddRange(calculation.Lines.Select(line => new DeclarationLine
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

            AccountingAudit.Record(
                db, pfa.Id, nameof(DeclarationVersion), version.Id, "GENERATE", null,
                new { type = calculation.Type, period = data.Period, amount = calculation.Total }, null, userId);
            generated.Add($"{calculation.Type} {AccountingJson.Amount(calculation.Total)} lei");
        }

        return (true, generated.Count > 0 ? $"Generate: {string.Join(", ", generated)}." : "Nicio declarație aplicabilă.");
    }
}

/// <summary>Rezumatul declarațiilor unei luni: din versiunea curentă sau, fără declarație, din pre-check.</summary>
internal static class DeclarationSummaries
{
    /// <summary>Versiunea curentă (cea mai mare) a fiecărei declarații.</summary>
    internal sealed record CurrentVersion(Guid DeclarationId, Guid PfaId, DeclarationType Type, Guid VersionId, int VersionNo, DeclarationVersionKind Kind, DeclarationStatus Status, decimal Amount);

    public static async Task<List<CurrentVersion>> CurrentVersionsAsync(IApplicationDbContext db, string period, IReadOnlyCollection<Guid> pfaIds, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. pfaIds];
        return await db.DeclarationVersions
            .AsNoTracking()
            .Where(v => v.Declaration.Period == period && ids.Contains(v.Declaration.PfaRegistrationId))
            .Where(v => v.VersionNo == v.Declaration.Versions.Max(other => other.VersionNo))
            .Select(v => new CurrentVersion(v.DeclarationId, v.Declaration.PfaRegistrationId, v.Declaration.Type, v.Id, v.VersionNo, v.Kind, v.Status, v.Amount))
            .ToListAsync(cancellationToken);
    }

    public static IReadOnlyList<DeclarationSummary> For(
        ScopePfa pfa,
        string period,
        PfaMonthCheck? check,
        IEnumerable<CurrentVersion> versions,
        Func<TaxResult> preview)
    {
        List<CurrentVersion> own = [.. versions.Where(v => v.PfaId == pfa.Id)];
        List<string> reasons = check is null ? [] : AccountingJson.Deserialize<List<string>>(check.ReasonsJson, []);
        TaxResult? calculated = null;

        return [.. Enum.GetValues<DeclarationType>().Select(type =>
        {
            CurrentVersion? version = own.FirstOrDefault(v => v.Type == type);
            if (version is not null)
            {
                return new DeclarationSummary(version.DeclarationId, pfa.Id, period, type, version.Status, version.Amount, version.VersionId, version.VersionNo, version.Kind, []);
            }

            if (check is null || own.Count > 0)
            {
                // Neprocesat, sau luna generată fără declarația acestui tip (neaplicabilă).
                DeclarationStatus? status = own.Count > 0 ? DeclarationStatus.NotApplicable : null;
                return new DeclarationSummary(null, pfa.Id, period, type, status, null, null, null, null, []);
            }

            switch (check.Status)
            {
                case PfaMonthStatus.MissingDocuments:
                    return new DeclarationSummary(null, pfa.Id, period, type, DeclarationStatus.BlockedMissingDocuments, null, null, null, null, reasons);
                case PfaMonthStatus.NeedsReview:
                    return new DeclarationSummary(null, pfa.Id, period, type, DeclarationStatus.BlockedNeedsReview, null, null, null, null, reasons);
                default:
                    // Gata, încă negenerată: suma e previzualizarea pe regulile de azi.
                    calculated ??= preview();
                    DeclarationCalculation calculation = calculated.Declarations[type];
                    return calculation.Applicable
                        ? new DeclarationSummary(null, pfa.Id, period, type, DeclarationStatus.Draft, calculation.Total, null, null, null, [])
                        : new DeclarationSummary(null, pfa.Id, period, type, DeclarationStatus.NotApplicable, null, null, null, null, []);
            }
        })];
    }
}
