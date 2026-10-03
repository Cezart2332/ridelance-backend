using Application.Abstractions.Authentication;
using Application.Accounting;
using Application.Accounting.Annual;
using Application.Accounting.Contracts;
using Application.Accounting.FiscalRegister;
using Application.Accounting.Ledger;
using Application.Accounting.Tax;
using Application.FiscalEstimates;
using Domain.Accounting;
using Domain.Documents;
using Domain.FiscalProfiles;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>Spec declarații F30–F61: D207, D205, D212 și C801, cu scenariile 9–14.</summary>
public sealed class AnnualDeclarationTests : IDisposable
{
    private static readonly TaxYearParameters Parameters2026 = new TaxYearParametersProvider().For(2026)!;

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly Guid _pfa = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();

    public AnnualDeclarationTests()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "ion@ridelance.ro", FirstName = "Ion", LastName = "Popescu" };
        _db.Users.Add(new User { Id = _admin, Email = "admin@ridelance.ro", FirstName = "Admin", LastName = "RIDElance" });
        _db.PfaRegistrations.Add(new PfaRegistration { Id = _pfa, UserId = user.Id, User = user, FullName = "Ion Popescu", Cui = "12345674" });
        _db.TaxRules.AddRange(TaxRuleSeed.Rules);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // ---------- D207 ----------

    private static D207Payment Payment(string taxId, decimal gross, decimal rate, decimal tax, int month = 3, NonResidentDecisionStatus status = NonResidentDecisionStatus.Auto) =>
        new(Guid.NewGuid(), new DateOnly(2026, month, 5), taxId == "EE102090374" ? "Bolt Operations OÜ" : "Uber B.V.", taxId[..2], taxId, "COMMISSION", gross, rate, tax, status, "Tratat");

    /// <summary>F30: un rând pe beneficiar, cu venitul brut, impozitul și numărul de plăți ale anului.</summary>
    [Fact]
    public void F30_D207AggregatesTheYearPerBeneficiary()
    {
        AnnualCalculation<D207DataModel> d207 = D207Engine.Build(
            2026,
            [Payment("EE102090374", 350, 2, 7), Payment("EE102090374", 150, 2, 3, 4), Payment("NL852071589B01", 400, 2, 8)],
            [new("2026-03", "EE102090374", 7), new("2026-04", "EE102090374", 3), new("2026-03", "NL852071589B01", 8)],
            900);

        d207.IsBlocked.ShouldBeFalse();
        d207.Model.Beneficiaries.Select(b => (b.TaxId, b.Country, b.GrossIncome, b.TaxWithheld, b.Payments))
            .ShouldBe([("EE102090374", "EE", 500m, 10m, 2), ("NL852071589B01", "NL", 400m, 8m, 1)]);
        (d207.Model.TotalGross, d207.Model.TotalTax).ShouldBe((900m, 18m));
    }

    /// <summary>F31: venitul scutit (impozit 0) intră în D207, cu suma scutită separat.</summary>
    [Fact]
    public void F31_ExemptIncomeIsIncluded()
    {
        AnnualCalculation<D207DataModel> d207 = D207Engine.Build(2026, [Payment("NL852071589B01", 400, 0, 0)], [new("2026-03", "NL852071589B01", 0)], 400);

        D207Beneficiary uber = d207.Model.Beneficiaries.ShouldHaveSingleItem();
        (uber.GrossIncome, uber.TaxWithheld, uber.ExemptIncome).ShouldBe((400m, 0m, 400m));
        d207.IsBlocked.ShouldBeFalse();
    }

    /// <summary>F32, scenariul 9: Σ D100 pe an ≠ totalul D207 pentru un beneficiar → Stop D207.</summary>
    [Fact]
    public void F32_S9_ADifferenceToTheMonthlyD100StopsD207()
    {
        AnnualCalculation<D207DataModel> d207 = D207Engine.Build(
            2026, [Payment("EE102090374", 350, 2, 7), Payment("EE102090374", 150, 2, 3, 4)], [new("2026-03", "EE102090374", 7)], 500);

        d207.IsBlocked.ShouldBeTrue();
        d207.Blockers.ShouldHaveSingleItem().ShouldContain("în D100 lunare 7,00 lei");
    }

    /// <summary>F32: Σ plăților din registru trebuie să fie Σ comisioanelor decontate; o decizie de confirmat oprește D207.</summary>
    [Fact]
    public void F32_PaymentsMustMatchTheSettledCommissionsAndBeConfirmed()
    {
        D207Engine.Build(2026, [Payment("EE102090374", 350, 2, 7)], [new("2026-03", "EE102090374", 7)], 500)
            .Blockers.ShouldHaveSingleItem().ShouldContain("comisioanele decontate 500,00 lei");
        D207Engine.Build(2026, [Payment("EE102090374", 350, 16, 56, status: NonResidentDecisionStatus.NeedsLegalConfirmation)], [], 350)
            .Blockers.ShouldContain("O regulă de nerezident e de confirmat.");
    }

    // ---------- D205 ----------

    private TaxRule RentRule(bool confirmed = true)
    {
        TaxRule rule = _db.TaxRules.Single(r => r.RuleType == TaxRuleTypes.RentWithholding);
        rule.Confirmed = confirmed;
        _db.SaveChanges();
        return rule;
    }

    private async Task<Guid> ContractAsync(Guid ruleId)
    {
        var handler = new CreateRentalContractCommandHandler(_db, new PlainSecrets(), new FixedUser(_admin));
        Result<Guid> created = await handler.Handle(
            new CreateRentalContractCommand(_pfa, "Maria Ionescu", "1800101221144", "12/2026", new DateOnly(2026, 1, 2), 1000, "monthly", ruleId),
            CancellationToken.None);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Description : null);
        return created.Value;
    }

    /// <summary>F41, scenariul 10: plata chiriei dă un rând D100 în luna plății, după regula de chirie.</summary>
    [Fact]
    public async Task F41_S10_ARentPaymentIsAD100LineInThePaymentMonth()
    {
        Guid contract = await ContractAsync(RentRule().Id);
        await new AddRentPaymentCommandHandler(_db, new FixedUser(_admin)).Handle(new AddRentPaymentCommand(contract, new DateOnly(2026, 3, 10), 1000), CancellationToken.None);

        ILookup<Guid, RentWithholding> march = await RentLines.LoadAsync(_db, null, [_pfa], new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), CancellationToken.None);
        (await RentLines.LoadAsync(_db, null, [_pfa], new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), CancellationToken.None))[_pfa].ShouldBeEmpty();
        RentWithholding line = march[_pfa].ShouldHaveSingleItem();
        (line.Gross, line.Rate, line.Tax).ShouldBe((1000m, 10m, 100m));

        Application.Accounting.Tax.TaxResult result = MonthlyTaxEngine.Calculate(March(line));
        DeclarationCalculation d100 = result.Declarations[DeclarationType.D100];
        d100.Lines.ShouldHaveSingleItem().RuleCode.ShouldBe(MonthlyTaxEngine.D100RentRule);
        d100.Total.ShouldBe(100m);
    }

    /// <summary>F41: regula de chirie neconfirmată juridic (Q3) oprește D100, nu o calculează pe ghicite.</summary>
    [Fact]
    public async Task F41_AnUnconfirmedRentRuleHoldsTheD100()
    {
        Guid contract = await ContractAsync(RentRule(confirmed: false).Id);
        await new AddRentPaymentCommandHandler(_db, new FixedUser(_admin)).Handle(new AddRentPaymentCommand(contract, new DateOnly(2026, 3, 10), 1000), CancellationToken.None);
        RentWithholding line = (await RentLines.LoadAsync(_db, null, [_pfa], new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), CancellationToken.None))[_pfa].Single();

        DeclarationCalculation d100 = MonthlyTaxEngine.Calculate(March(line)).Declarations[DeclarationType.D100];
        d100.IsBlocked.ShouldBeTrue();
        d100.Blockers.ShouldNotBeNull().ShouldContain(b => b.Contains("de confirmat juridic", StringComparison.Ordinal));
    }

    /// <summary>F40, F42, scenariul 10: D205 doar pentru PFA-ul cu contract, pe beneficiar, din plățile anului.</summary>
    [Fact]
    public async Task F40_F42_S10_D205ExistsOnlyWithARentalContract()
    {
        AnnualDeclarationService annual = Service();
        (await annual.LoadAsync(_pfa, 2026, CancellationToken.None)).D205.ShouldBeNull();

        Guid contract = await ContractAsync(RentRule().Id);
        var add = new AddRentPaymentCommandHandler(_db, new FixedUser(_admin));
        await add.Handle(new AddRentPaymentCommand(contract, new DateOnly(2026, 3, 10), 1000), CancellationToken.None);
        await add.Handle(new AddRentPaymentCommand(contract, new DateOnly(2026, 4, 10), 1000), CancellationToken.None);

        AnnualCalculation<D205DataModel> d205 = (await annual.LoadAsync(_pfa, 2026, CancellationToken.None)).D205.ShouldNotBeNull();
        D205Beneficiary owner = d205.Model.Beneficiaries.ShouldHaveSingleItem();
        (owner.OwnerName, owner.GrossIncome, owner.TaxWithheld, owner.Payments).ShouldBe(("Maria Ionescu", 2000m, 200m, 2));
        owner.OwnerCnpMasked.ShouldNotContain("1800101221144");
        // Termenul D205 e încă neconfirmat în seed (Q3).
        d205.Blockers.ShouldContain("Termenul D205 e de confirmat juridic.");
    }

    /// <summary>F43: contractul de chirie nu poate folosi o regulă de nerezident; CNP-ul se salvează criptat.</summary>
    [Fact]
    public async Task F43_ARentContractRefusesANonResidentRule()
    {
        TaxRule nonResident = await _db.TaxRules.SingleAsync(r => r.RuleType == TaxRuleTypes.NonResidentRate);
        Result<Guid> refused = await new CreateRentalContractCommandHandler(_db, new ReversedSecrets(), new FixedUser(_admin)).Handle(
            new CreateRentalContractCommand(_pfa, "Maria Ionescu", "1800101221144", "12/2026", new DateOnly(2026, 1, 2), 1000, "MONTHLY", nonResident.Id),
            CancellationToken.None);
        refused.Error.Code.ShouldBe("Accounting.RentalContractRule");
        Should.Throw<TaxRuleConfigurationException>(() => RentEngine.EnsureRentRule(nonResident));

        Guid id = (await new CreateRentalContractCommandHandler(_db, new ReversedSecrets(), new FixedUser(_admin)).Handle(
            new CreateRentalContractCommand(_pfa, "Maria Ionescu", "1800101221144", "12/2026", new DateOnly(2026, 1, 2), 1000, "MONTHLY", RentRule().Id),
            CancellationToken.None)).Value;
        (await _db.RentalContracts.SingleAsync(c => c.Id == id)).OwnerCnpEncrypted.ShouldNotBe("1800101221144");
    }

    // ---------- D212 ----------

    private static PersonalTaxProfile Profile(bool? external = false, bool supplement = false, decimal? prefilled = null, decimal? history = null, ProfileFlags? flags = null) =>
        new(2026, flags ?? new ProfileFlags(), external, supplement, prefilled, history);

    private static AnnualCalculation<D212Calculation> D212(PersonalTaxProfile? profile, AnnualTotals? refFinal = null, AnnualTotals? recomputed = null)
    {
        var totals = new AnnualTotals(80_000, 20_000);
        return AnnualTaxEngine.Calculate(new AnnualTaxInput(2026, refFinal ?? totals, recomputed ?? totals, profile, Parameters2026, 2027), new TaxEngine2026());
    }

    /// <summary>F51: CAS, CASS, partea deductibilă, venitul impozabil și impozitul, cu parametrii anului fiscal.</summary>
    [Fact]
    public void F51_TheAnnualEngineComputesContributionsAndTax()
    {
        AnnualCalculation<D212Calculation> d212 = D212(Profile());

        d212.IsBlocked.ShouldBeFalse();
        D212DataModel model = d212.Model.Model.ShouldNotBeNull();
        (model.GrossIncome, model.DeductibleExpenses, model.NetIncome).ShouldBe((80_000m, 20_000m, 60_000m));
        (model.CasBase, model.CasDue).ShouldBe((48_600m, 12_150m));
        (model.CassBase, model.CassDue, model.CassDeductible).ShouldBe((60_000m, 6_000m, 6_000m));
        (model.IncomeTaxBase, model.IncomeTaxDue).ShouldBe((41_850m, 4_185m));
        model.RuleVersion.ShouldBe(Parameters2026.RuleVersion);
    }

    /// <summary>F50, scenariul 12: brutul sau deductibilul diferit de REF-ul final → Stop; anul neînchis → Stop.</summary>
    [Fact]
    public void F50_S12_ADifferenceFromTheFinalRefStopsD212()
    {
        D212(Profile(), recomputed: new AnnualTotals(81_000, 20_000)).Blockers.ShouldHaveSingleItem().ShouldContain("REF-ul final 2026");
        AnnualTaxEngine.Calculate(new AnnualTaxInput(2026, null, new AnnualTotals(80_000, 20_000), Profile(), Parameters2026, 2027), new TaxEngine2026())
            .Blockers.ShouldHaveSingleItem().ShouldContain("nu e final");
    }

    /// <summary>F52: pierderile din D212 anterioară se compensează; fără istoric complet → NeedsReview, fără model.</summary>
    [Fact]
    public void F52_CarriedLossesComeFromHistoryOrNeedReview()
    {
        D212DataModel withHistory = D212(Profile(history: 10_000)).Model.Model.ShouldNotBeNull();
        (withHistory.CarriedLosses, withHistory.IncomeTaxBase, withHistory.IncomeTaxDue).ShouldBe((10_000m, 31_850m, 3_185m));

        AnnualCalculation<D212Calculation> noHistory = D212(Profile(flags: new ProfileFlags { CarriedLosses = true }));
        noHistory.IsBlocked.ShouldBeFalse();
        noHistory.Review.ShouldHaveSingleItem().ShouldContain("Pierderile reportate");
        noHistory.Model.Model.ShouldBeNull();
    }

    /// <summary>F53, scenariul 13: fără răspuns despre alte venituri D212 e blocată; „Da” cere formularul suplimentar.</summary>
    [Fact]
    public void F53_S13_TheExternalIncomeQuestionGatesD212()
    {
        D212(Profile(external: null)).Blockers.ShouldHaveSingleItem().ShouldContain("alte venituri");
        D212(Profile(external: true)).Blockers.ShouldHaveSingleItem().ShouldContain("Formularul suplimentar");
        D212(Profile(external: true, supplement: true)).IsBlocked.ShouldBeFalse();
        D212(Profile(external: false)).IsBlocked.ShouldBeFalse();
    }

    /// <summary>F54, scenariul 14: precompletarea ANAF e control — egal → Match, diferit → NeedsReview, calculul rămâne.</summary>
    [Fact]
    public void F54_S14_TheAnafPrefillIsOnlyAControl()
    {
        D212(Profile(prefilled: 60_000)).Model.Prefill.ShouldBe(PrefillCheck.Match);

        AnnualCalculation<D212Calculation> differs = D212(Profile(prefilled: 58_000));
        differs.Model.Prefill.ShouldBe(PrefillCheck.NeedsReview);
        differs.Review.ShouldHaveSingleItem().ShouldContain("58.000,00");
        differs.Model.Model.ShouldNotBeNull().NetIncome.ShouldBe(60_000m);
    }

    /// <summary>F55, scenariul 11: anul fiscal 2026 se depune în 2027 (termenul D212), cu adaptorul formularului 2027.</summary>
    [Fact]
    public async Task F55_S11_TaxYear2026UsesThe2027Form()
    {
        DateOnly due = DeclarationDeadline.Of(new TaxRuleSet(TaxRuleSeed.Rules).Find(TaxRuleTypes.Deadline, "RO", new DateOnly(2026, 12, 31), new TaxRuleContext("D212"))!.Formula!, "2026");
        due.Year.ShouldBe(2027);
        ID212FormAdapter form = D212Forms.For(due.Year).ShouldNotBeNull();
        form.ShouldBeOfType<D212Form2027>();
        D212Forms.For(2026).ShouldBeNull();

        D212DataModel model = D212(Profile()).Model.Model!;
        form.Map(model).ShouldContain(field => field.Label == "Impozit pe venit datorat" && field.Value == 4_185m);

        AnnualData data = await Service().LoadAsync(_pfa, 2026, CancellationToken.None);
        data.Form.ShouldBeOfType<D212Form2027>();
        (await Service().LoadAsync(_pfa, 2027, CancellationToken.None)).D212.Blockers.ShouldContain("Formularul D212 pentru depunerea din 2028 nu e configurat.");
    }

    /// <summary>
    /// F50, F53, F55 cap-coadă: anul închis, profilul completat, răspunsul „Nu” → D212 generată (perioada „2026”),
    /// validată fără XML (depunere manuală), iar recalculul dă același hash.
    /// </summary>
    [Fact]
    public async Task F55_D212IsGeneratedValidatedAndHashedForAClosedYear()
    {
        await CloseYearAsync();
        AnnualDeclarationService annual = Service();
        (await annual.LoadAsync(_pfa, 2026, CancellationToken.None)).D212.Blockers.ShouldContain("Lipsește răspunsul despre alte venituri sau contribuții în afara RIDElance.");

        await new AnswerExternalIncomeCommandHandler(_db, new FixedUser(_pfaUser)).Handle(new AnswerExternalIncomeCommand(2026, false), CancellationToken.None);
        (await annual.GenerateAsync(_pfa, 2026, _admin, CancellationToken.None)).ShouldBe([DeclarationType.D212]);
        (await annual.GenerateAsync(_pfa, 2026, _admin, CancellationToken.None)).ShouldBeEmpty();

        DeclarationVersion version = await _db.DeclarationVersions.Include(v => v.Declaration).SingleAsync();
        (version.Declaration.Period, version.Status).ShouldBe(("2026", DeclarationStatus.Generated));
        string hash = version.XmlHash.ShouldNotBeNull();
        AnnualDeclarationService.Apply(version, await annual.LoadAsync(_pfa, 2026, CancellationToken.None), DeclarationType.D212);
        version.XmlHash.ShouldBe(hash);

        await annual.ValidateAsync(version, _admin, CancellationToken.None);
        version.Status.ShouldBe(DeclarationStatus.ReadyToSign);
    }

    // ---------- C801 ----------

    /// <summary>F60: datele C801 — CUI-ul, casa de marcat, tipul de activitate din regulă și numărul de înmatriculare.</summary>
    [Fact]
    public async Task F60_C801DataComesFromThePfaAndTheRules()
    {
        _db.CashRegisterStates.Add(new CashRegisterState { PfaRegistrationId = _pfa, CashRequested = true, CashEnabled = true, Status = CashRegisterStatus.Active, ActivationDate = new DateOnly(2026, 2, 1) });
        await _db.SaveChangesAsync();

        C801Dto dto = (await new GetC801QueryHandler(_db).Handle(new GetC801Query(_pfa), CancellationToken.None)).Value;
        (dto.Cui, dto.ActivityType).ShouldBe(("12345674", "4"));
        dto.Missing.ShouldBe(["Numărul de înmatriculare"]);
    }

    /// <summary>F61: furnizorul a depus C801 → RIDElance păstrează doar starea, documentul și NUI-ul; nicio declarație.</summary>
    [Fact]
    public async Task F61_AProviderFiledC801IsOnlyRecorded()
    {
        _db.CashRegisterStates.Add(new CashRegisterState { PfaRegistrationId = _pfa, CashRequested = true, CashEnabled = true });
        var file = new Document { Id = Guid.NewGuid(), OriginalFileName = "c801.pdf", ContentType = "application/pdf", Origin = DocumentOrigin.AccountingUpload };
        _db.Documents.Add(file);
        await _db.SaveChangesAsync();
        var update = new UpdateC801CommandHandler(_db, new FixedUser(_admin));

        (await update.Handle(new UpdateC801Command(_pfa, C801Status.FiledByProvider, null, null, "B123ABC"), CancellationToken.None)).Error.Code.ShouldBe("Accounting.C801DocumentRequired");
        (await update.Handle(new UpdateC801Command(_pfa, C801Status.NuiReceived, file.Id, null, null), CancellationToken.None)).Error.Code.ShouldBe("Accounting.C801NuiRequired");
        C801Dto dto = (await update.Handle(new UpdateC801Command(_pfa, C801Status.NuiReceived, file.Id, "NUI-0001", "b123abc"), CancellationToken.None)).Value;

        (dto.Status, dto.DocumentId, dto.NuiNumber, dto.VehiclePlate).ShouldBe((C801Status.NuiReceived, (Guid?)file.Id, "NUI-0001", "B123ABC"));
        (await _db.Declarations.AnyAsync()).ShouldBeFalse();
    }

    // ---------- suport ----------

    private static PfaTaxInput March(RentWithholding line) =>
        new("2026-03", [], [], [], [], [], [], [], TaxEngineSettings.ForPeriod(new TaxRuleSet(TaxRuleSeed.Rules), "2026-03"), [], [line]);

    private Guid _pfaUser => _db.PfaRegistrations.Single(p => p.Id == _pfa).UserId;

    private AnnualDeclarationService Service() => new(_db, new PlainSecrets(), new TaxEngine2026(), new TaxYearParametersProvider());

    /// <summary>Anul 2026 închis: REF-ul final e cel calculat din ledger, profilul fiscal completat.</summary>
    private async Task CloseYearAsync()
    {
        RefView final = await GetRefQueryHandler.ComputeAsync(_db, _pfa, 2026, RefStatus.Current, null, CancellationToken.None);
        _db.AccountingYears.Add(new AccountingYear { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Year = 2026, Status = AccountingPeriodStatus.Closed, RefJson = AccountingJson.Serialize(final) });
        _db.PfaTaxProfiles.Add(new PfaTaxProfile { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, TaxYear = 2026, Status = PfaTaxProfileStatus.Completed, AnswersJson = "{}" });
        await _db.SaveChangesAsync();
    }

    private sealed class ReversedSecrets : Application.Abstractions.Security.ISecretProtector
    {
        public string Protect(string plainText) => new([.. plainText.Reverse()]);

        public string Unprotect(string protectedText) => new([.. protectedText.Reverse()]);
    }

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
