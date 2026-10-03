using System.Globalization;
using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Accounting.Contracts;
using Application.Accounting.FiscalRegister;
using Application.Accounting.Ledger;
using Application.Accounting.Months;
using Application.Accounting.Tax;
using Application.FiscalEstimates;
using Application.FiscalProfiles;
using Domain.Accounting;
using Domain.FiscalProfiles;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Annual;

/// <summary>Snapshot-ul unei versiuni de declarație anuală: modelul calculat, din care nu se mai recalculează.</summary>
internal sealed record AnnualSnapshot(
    DeclarationType Type,
    int Year,
    string RulesetVersion,
    D207DataModel? D207,
    D205DataModel? D205,
    D212DataModel? D212,
    IReadOnlyList<D212Field>? Form,
    string? FormVersion,
    IReadOnlyList<string> Review,
    DateTime CalculatedAtUtc)
{
    public static AnnualSnapshot? Read(string json)
    {
        AnnualSnapshot? snapshot = AccountingJson.Deserialize<AnnualSnapshot?>(json, null);
        return snapshot is { Year: > 0 } && DeclarationTypes.IsAnnual(snapshot.Type) ? snapshot : null;
    }
}

/// <summary>Toate declarațiile anuale ale unui PFA pe un an, calculate din aceleași date.</summary>
internal sealed record AnnualData(
    int Year,
    string RulesetVersion,
    AnnualCalculation<D207DataModel> D207,
    AnnualCalculation<D205DataModel>? D205,
    AnnualCalculation<D212Calculation> D212,
    ID212FormAdapter? Form,
    AnnualTaxAnswers? Answers,
    IReadOnlyList<RentWithholding> Rent)
{
    public IReadOnlyList<string> BlockersOf(DeclarationType type) => type switch
    {
        DeclarationType.D207 => D207.Blockers,
        DeclarationType.D205 => D205?.Blockers ?? ["PFA-ul nu are contract de închiriere cu o persoană fizică."],
        _ => D212.Blockers,
    };

    /// <summary>Se poate genera: fără blocaje și cu ceva de declarat.</summary>
    public bool IsReady(DeclarationType type) => BlockersOf(type).Count == 0 && type switch
    {
        DeclarationType.D207 => D207.Model.Beneficiaries.Count > 0,
        DeclarationType.D205 => D205 is { Model.Beneficiaries.Count: > 0 },
        _ => D212.Model.Model is not null,
    };

    public decimal AmountOf(DeclarationType type) => type switch
    {
        DeclarationType.D207 => D207.Model.TotalTax,
        DeclarationType.D205 => D205?.Model.TotalTax ?? 0,
        _ => D212.Model.Model is { } model ? model.CasDue + model.CassDue + model.IncomeTaxDue : 0,
    };

    public AnnualSnapshot Snapshot(DeclarationType type) => new(
        type,
        Year,
        RulesetVersion,
        type == DeclarationType.D207 ? D207.Model : null,
        type == DeclarationType.D205 ? D205?.Model : null,
        type == DeclarationType.D212 ? D212.Model.Model : null,
        type == DeclarationType.D212 && D212.Model.Model is { } model && Form is not null ? Form.Map(model) : null,
        type == DeclarationType.D212 ? Form?.FormVersion : null,
        type switch
        {
            DeclarationType.D207 => D207.Review,
            DeclarationType.D205 => D205?.Review ?? [],
            _ => D212.Review,
        },
        DateTime.UtcNow);
}

/// <summary>
/// Declarațiile anuale ale unui PFA (spec declarații §3 „Flux anual”, F30–F55): datele anului,
/// generarea înregistrărilor (perioada <c>yyyy</c>, fără XML: depunere manuală, Q4) și validarea lor.
/// Mașina de stări e aceeași ca la lunare; validarea anuală recalculează și compară cu snapshot-ul.
/// </summary>
internal sealed class AnnualDeclarationService(
    IApplicationDbContext db,
    ISecretProtector secrets,
    ITaxEngine engine,
    TaxYearParametersProvider parameters)
{
    public async Task<AnnualData> LoadAsync(Guid pfaId, int year, CancellationToken cancellationToken)
    {
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        TaxRuleSet rules = await TaxRuleSet.LoadAsync(db, cancellationToken);

        AnnualCalculation<D207DataModel> d207 = D207Engine.Build(
            year,
            await NonResidentPaymentsAsync(pfaId, start, end, cancellationToken),
            await DeclaredD100Async(pfaId, year, cancellationToken),
            await PaidCommissionsAsync(pfaId, start, end, cancellationToken));

        List<RentWithholding> rent = await RentAsync(pfaId, start, end, rules, cancellationToken);
        bool hasContract = await db.RentalContracts.AnyAsync(c => c.PfaRegistrationId == pfaId, cancellationToken);
        AnnualCalculation<D205DataModel>? d205 = hasContract
            ? RentEngine.D205(year, rent, rules.Find(TaxRuleTypes.Deadline, "RO", end, new TaxRuleContext(nameof(DeclarationType.D205)))?.Confirmed == true)
            : null;

        AnnualTaxAnswers? answers = await db.AnnualTaxAnswers.AsNoTracking()
            .SingleOrDefaultAsync(a => a.PfaRegistrationId == pfaId && a.TaxYear == year, cancellationToken);
        int? formYear = rules.Find(TaxRuleTypes.Deadline, "RO", end, new TaxRuleContext(nameof(DeclarationType.D212))) is { Formula: { } formula }
            ? DeclarationDeadline.Of(formula, year.ToString(CultureInfo.InvariantCulture)).Year
            : null;
        ID212FormAdapter? form = formYear is { } filing ? D212Forms.For(filing) : null;

        AnnualCalculation<D212Calculation> d212 = AnnualTaxEngine.Calculate(
            new AnnualTaxInput(
                year,
                await RefFinalAsync(pfaId, year, cancellationToken),
                Totals(await GetRefQueryHandler.ComputeAsync(db, pfaId, year, RefStatus.Current, null, cancellationToken)),
                await ProfileAsync(pfaId, year, answers, cancellationToken),
                parameters.For(year),
                formYear ?? 0),
            engine);
        if (form is null)
        {
            d212 = d212 with
            {
                Blockers = [.. d212.Blockers, formYear is { } missing
                    ? $"Formularul D212 pentru depunerea din {missing} nu e configurat."
                    : "Termenul D212 nu e configurat în regulile fiscale."],
            };
        }

        return new AnnualData(year, rules.VersionAt(end), d207, d205, d212, form, answers, rent);
    }

    /// <summary>Generează înregistrările anuale gata de depunere; cele deja generate rămân (corecția e rectificativă).</summary>
    public async Task<IReadOnlyList<DeclarationType>> GenerateAsync(Guid pfaId, int year, Guid? userId, CancellationToken cancellationToken)
    {
        await NonResident.NonResidentSync.SyncAsync(db, pfaId, cancellationToken);
        AnnualData data = await LoadAsync(pfaId, year, cancellationToken);
        string period = year.ToString(CultureInfo.InvariantCulture);
        HashSet<DeclarationType> existing = [.. await db.Declarations
            .Where(d => d.PfaRegistrationId == pfaId && d.Period == period)
            .Select(d => d.Type)
            .ToListAsync(cancellationToken)];

        var generated = new List<DeclarationType>();
        foreach (DeclarationType type in DeclarationTypes.Annual.Where(t => !existing.Contains(t) && data.IsReady(t)))
        {
            var declaration = new Declaration { Id = Guid.NewGuid(), PfaRegistrationId = pfaId, Period = period, Type = type };
            var version = new DeclarationVersion
            {
                Id = Guid.NewGuid(),
                DeclarationId = declaration.Id,
                VersionNo = 1,
                Kind = DeclarationVersionKind.Initial,
                Status = DeclarationStatus.Generated,
                StatusHistoryJson = AccountingJson.Serialize(new[] { new StatusHistoryRecord(null, DeclarationStatus.Generated, DateTime.UtcNow, userId, null) }),
                CreatedByUserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
            };
            Apply(version, data, type);
            db.Declarations.Add(declaration);
            db.DeclarationVersions.Add(version);
            AccountingAudit.Record(db, pfaId, nameof(DeclarationVersion), version.Id, "GENERATE", null, new { type, period, amount = version.Amount }, null, userId);
            generated.Add(type);
        }

        await db.SaveChangesAsync(cancellationToken);
        return generated;
    }

    /// <summary>Calculul de acum al unei declarații anuale, sau de ce nu se poate.</summary>
    public async Task<Result<AnnualData>> DraftAsync(Declaration declaration, CancellationToken cancellationToken)
    {
        await NonResident.NonResidentSync.SyncAsync(db, declaration.PfaRegistrationId, cancellationToken);
        AnnualData data = await LoadAsync(declaration.PfaRegistrationId, int.Parse(declaration.Period, CultureInfo.InvariantCulture), cancellationToken);
        IReadOnlyList<string> blockers = data.BlockersOf(declaration.Type);
        return blockers.Count > 0
            ? Result.Failure<AnnualData>(Error.Conflict("Accounting.AnnualBlocked", string.Join(" ", blockers)))
            : data;
    }

    public static void Apply(DeclarationVersion version, AnnualData data, DeclarationType type)
    {
        version.Amount = data.AmountOf(type);
        version.SnapshotJson = AccountingJson.Serialize(data.Snapshot(type));
        version.RulesetVersion = data.RulesetVersion;
        version.XmlHash = Declarations.DeclarationFiles.Hash(System.Text.Encoding.UTF8.GetBytes(AccountingJson.Serialize(Comparable(data.Snapshot(type)))));
    }

    /// <summary>
    /// Validarea anuală: datele de acum trebuie să dea același model ca snapshot-ul (altfel „Regenerează”)
    /// și să nu aibă blocaje. Fără XML: formularul se depune manual, deci nu există nivel XSD.
    /// </summary>
    public async Task<Result> ValidateAsync(DeclarationVersion version, Guid? userId, CancellationToken cancellationToken)
    {
        Declaration declaration = version.Declaration;
        Result<AnnualData> draft = await DraftAsync(declaration, cancellationToken);
        var messages = new List<ValidationMessage>();
        if (draft.IsFailure)
        {
            messages.Add(new ValidationMessage(null, draft.Error.Description));
        }
        else if (AnnualSnapshot.Read(version.SnapshotJson) is not { } stored ||
                 AccountingJson.Serialize(Comparable(stored)) != AccountingJson.Serialize(Comparable(draft.Value.Snapshot(declaration.Type))))
        {
            messages.Add(new ValidationMessage(null, "Datele anului s-au schimbat după generare: declarația trebuie regenerată („Regenerează”)."));
        }

        ValidationLevelResult level = new(ValidationLevel.Ridelance, messages.Count == 0, messages);
        version.ValidationResultJson = AccountingJson.Serialize(new Declarations.StoredValidation([level], DateTime.UtcNow, null, null, null));
        DeclarationStatus from = version.Status;
        if (messages.Count == 0)
        {
            Declarations.DeclarationStateMachine.Move(version, DeclarationStatus.Validated, userId, null);
            Declarations.DeclarationStateMachine.Move(version, DeclarationStatus.ReadyToSign, userId, "Depunere manuală (aplicația web ANAF / PDF)");
        }
        else
        {
            Declarations.DeclarationStateMachine.Move(version, DeclarationStatus.ValidationFailed, userId, messages[0].Text);
        }

        AccountingAudit.Record(db, declaration.PfaRegistrationId, nameof(DeclarationVersion), version.Id, "VALIDATE", new { status = from }, new { status = version.Status }, null, userId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Snapshot-ul fără momentul calculului: aceleași date → același hash.</summary>
    private static AnnualSnapshot Comparable(AnnualSnapshot snapshot) => snapshot with { CalculatedAtUtc = default };

    private async Task<List<D207Payment>> NonResidentPaymentsAsync(Guid pfaId, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var rows = await db.NonResidentPayments.AsNoTracking()
            .Where(p => p.PfaRegistrationId == pfaId && p.PaymentDate >= start && p.PaymentDate <= end)
            .Join(db.NonResidentTaxDecisions, p => p.Id, d => d.PaymentId, (p, d) => new
            {
                Payment = p,
                d.TaxRate,
                d.TaxDue,
                d.Status,
                Treaty = db.SupplierTaxProfiles.Where(s => s.Id == d.ResidenceCertificateProfileId).Select(s => s.Treaty).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new D207Payment(
            r.Payment.Id, r.Payment.PaymentDate, r.Payment.SupplierLegalName, r.Payment.SupplierCountry, r.Payment.SupplierTaxId,
            r.Payment.IncomeType, r.Payment.GrossIncomeRon, r.TaxRate, r.TaxDue, r.Status, r.Treaty))];
    }

    /// <summary>Impozitul pe beneficiar din D100 lunare ale anului (versiunea curentă a fiecăreia).</summary>
    private async Task<List<D100Declared>> DeclaredD100Async(Guid pfaId, int year, CancellationToken cancellationToken)
    {
        string prefix = $"{year:D4}-";
        var lines = await db.DeclarationVersions.AsNoTracking()
            .Where(v => v.Declaration.PfaRegistrationId == pfaId &&
                        v.Declaration.Type == DeclarationType.D100 &&
                        v.Declaration.Period.StartsWith(prefix) &&
                        v.VersionNo == v.Declaration.Versions.Max(other => other.VersionNo))
            .SelectMany(v => v.Lines
                .Where(l => l.SupersededAtUtc == null && l.RuleCode == MonthlyTaxEngine.D100CommissionRule)
                .Select(l => new { v.Declaration.Period, l.SupplierVatId, l.Value }))
            .ToListAsync(cancellationToken);
        return [.. lines.Select(l => new D100Declared(l.Period, l.SupplierVatId ?? string.Empty, l.Value))];
    }

    /// <summary>Comisioanele reținute la decontare în an (plățile efective către nerezidenți, ledger R21).</summary>
    private async Task<decimal?> PaidCommissionsAsync(Guid pfaId, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        List<decimal> amounts = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfaId &&
                        e.Category == LedgerSupport.PlatformCommissionCategory &&
                        e.SettlementGroupId != null &&
                        e.StornoOfEntryId == null &&
                        e.PlatformDocumentId != null &&
                        e.Date >= start && e.Date <= end)
            .Select(e => e.Amount)
            .ToListAsync(cancellationToken);
        return amounts.Count == 0 ? null : amounts.Sum(Math.Abs);
    }

    private async Task<List<RentWithholding>> RentAsync(Guid pfaId, DateOnly start, DateOnly end, TaxRuleSet rules, CancellationToken cancellationToken)
    {
        var rows = await db.RentPayments.AsNoTracking()
            .Where(p => p.PaymentDate >= start && p.PaymentDate <= end)
            .Join(db.RentalContracts.Where(c => c.PfaRegistrationId == pfaId), p => p.RentalContractId, c => c.Id, (p, c) => new { Payment = p, Contract = c })
            .ToListAsync(cancellationToken);
        return [.. rows
            .OrderBy(r => r.Payment.PaymentDate)
            .Select(r => RentEngine.Withhold(
                r.Contract,
                r.Payment,
                rules.Rules.SingleOrDefault(rule => rule.Id == r.Contract.WithholdingRuleId)
                    ?? throw new TaxRuleConfigurationException($"Regula de reținere {r.Contract.WithholdingRuleId} a contractului {r.Contract.ContractNumber} nu există."),
                RentalContracts.MaskCnp(secrets, r.Contract.OwnerCnpEncrypted)))];
    }

    private async Task<AnnualTotals?> RefFinalAsync(Guid pfaId, int year, CancellationToken cancellationToken)
    {
        string? json = await db.AccountingYears.AsNoTracking()
            .Where(y => y.PfaRegistrationId == pfaId && y.Year == year && y.Status == AccountingPeriodStatus.Closed)
            .Select(y => y.RefJson)
            .FirstOrDefaultAsync(cancellationToken);
        return AccountingJson.Deserialize<RefView?>(json, null) is { } view ? Totals(view) : null;
    }

    /// <summary>Primele două rânduri ale REF: venitul brut și cheltuielile deductibile.</summary>
    private static AnnualTotals Totals(RefView view) => new(view.Rows[0].Value, view.Rows[1].Value);

    private async Task<PersonalTaxProfile?> ProfileAsync(Guid pfaId, int year, AnnualTaxAnswers? answers, CancellationToken cancellationToken)
    {
        PfaTaxProfile? profile = await db.PfaTaxProfiles.AsNoTracking()
            .SingleOrDefaultAsync(p => p.PfaRegistrationId == pfaId && p.TaxYear == year && p.Status == PfaTaxProfileStatus.Completed, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        // F52: pierderea de reportat din D212 a anului anterior, dacă a fost depusă prin RIDElance.
        string previous = (year - 1).ToString(CultureInfo.InvariantCulture);
        string? prior = await db.DeclarationVersions.AsNoTracking()
            .Where(v => v.Declaration.PfaRegistrationId == pfaId && v.Declaration.Type == DeclarationType.D212 && v.Declaration.Period == previous &&
                        v.VersionNo == v.Declaration.Versions.Max(other => other.VersionNo))
            .Select(v => v.SnapshotJson)
            .FirstOrDefaultAsync(cancellationToken);

        return new PersonalTaxProfile(
            year,
            ProfileFlagsMapper.Map(FiscalProfileService.Deserialize(profile.AnswersJson), year, false),
            answers?.HasExternalIncome,
            answers?.SupplementCompleted ?? false,
            answers?.AnafPrefilledNetIncome,
            prior is null ? null : AnnualSnapshot.Read(prior)?.D212?.LossCarriedForward);
    }
}
