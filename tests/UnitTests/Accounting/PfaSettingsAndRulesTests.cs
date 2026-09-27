using System.Text.Json;
using Application.Abstractions.Authentication;
using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.Ledger;
using Application.Accounting.Pfas;
using Application.Accounting.Rules;
using Domain.Accounting;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Accounting.Anaf;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>Ce lipsea din contract: lista PFA, setările, casa de marcat, preferința de numerar, regulile fiscale.</summary>
public sealed class PfaSettingsAndRulesTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly Guid _accountant = Guid.NewGuid();
    private readonly Guid _ionUser = Guid.NewGuid();
    private readonly Guid _ion = Guid.NewGuid();
    private readonly Guid _stefan = Guid.NewGuid();

    public PfaSettingsAndRulesTests()
    {
        _db.Users.Add(new User { Id = _accountant, Email = "contabil@ridelance.ro", FirstName = "Contabil", LastName = "RIDElance", Role = UserRole.Contabil });
        Pfa(_ion, _ionUser, "Ion Popescu", "12345674");
        Pfa(_stefan, Guid.NewGuid(), "Ștefan Ionescu", "41000001");
        _db.PfaAccountingEngagements.Add(new PfaAccountingEngagement
        {
            Id = Guid.NewGuid(), PfaRegistrationId = _stefan, StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2026, 3, 31), Status = EngagementStatus.Inactive,
        });
        _db.ExpenseCategoryRules.Add(new ExpenseCategoryRule { Id = Guid.NewGuid(), Category = "FUEL", Label = "Combustibil", VehicleRelated = true, DefaultDeductibility = DeductibilityType.Percent100, ValidFrom = new DateOnly(2025, 1, 1) });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Pfa_list_filters_by_status_and_searches_without_diacritics()
    {
        _db.CashRegisterStates.Add(new CashRegisterState { PfaRegistrationId = _ion, Status = CashRegisterStatus.Pending });
        await _db.SaveChangesAsync();
        var handler = new ListPfasQueryHandler(_db);

        IReadOnlyList<PfaListItem> all = (await handler.Handle(new ListPfasQuery(null, null), CancellationToken.None)).Value;
        IReadOnlyList<PfaListItem> active = (await handler.Handle(new ListPfasQuery("active", null), CancellationToken.None)).Value;
        IReadOnlyList<PfaListItem> search = (await handler.Handle(new ListPfasQuery(null, "stefan"), CancellationToken.None)).Value;

        // Fără denumire din certificat: numele titularului + „PFA” (PfaNames).
        all.Select(p => p.Name).ShouldBe(["Ion Popescu PFA", "Ștefan Ionescu PFA"]);
        active.ShouldHaveSingleItem().CashStatus.ShouldBe(CashRegisterStatus.Pending);
        search.ShouldHaveSingleItem().EngagementStatus.ShouldBe(EngagementStatus.Inactive);
        (await handler.Handle(new ListPfasQuery(null, "4100"), CancellationToken.None)).Value.ShouldHaveSingleItem().Id.ShouldBe(_stefan);
    }

    [Fact]
    public async Task A_platforms_change_valid_from_today_shows_right_away()
    {
        // Luna fiscală în lucru e luna trecută; setările afișează totuși valoarea de azi, altfel
        // schimbarea părea nesalvată.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await Update(PfaAccountingSettingKeys.Platforms, "[\"BOLT\",\"UBER\"]", new DateOnly(2025, 1, 1), "Onboarding")).IsSuccess.ShouldBeTrue();

        PfaAccountingSettingsDto settings = (await Update(PfaAccountingSettingKeys.Platforms, "[\"UBER\"]", today, "A renunțat la Bolt")).Value;

        settings.Platforms.ShouldBe([Platform.Uber]);
    }

    [Fact]
    public async Task Art317_vat_code_is_its_own_setting_and_must_be_a_valid_ro_code()
    {
        PfaAccountingSettingsDto settings = (await Update(PfaAccountingSettingKeys.Art317VatCode, "\"ro 5132 1900\"", new DateOnly(2026, 1, 1), "Certificat TVA")).Value;

        // Codul decide și regimul art. 317: activ de la aceeași dată.
        settings.Art317.ShouldBe(new Art317Setting(true, new DateOnly(2026, 1, 1), "RO51321900"));
        PfaAccountingSettingsDto removed = (await Update(PfaAccountingSettingKeys.Art317VatCode, "\"\"", new DateOnly(2026, 3, 1), "Cod anulat")).Value;
        removed.Art317.ShouldBe(new Art317Setting(false, null, null));
        (await Update(PfaAccountingSettingKeys.Art317VatCode, "\"51321900\"", new DateOnly(2026, 2, 1), "Fără RO")).Error.Code.ShouldBe("Accounting.InvalidVatCode");
        (await Update(PfaAccountingSettingKeys.Art317VatCode, "\"RO51321901\"", new DateOnly(2026, 2, 1), "Cifra de control greșită")).Error.Code.ShouldBe("Accounting.InvalidVatCode");
    }

    [Fact]
    public async Task A_wrong_unused_supplier_can_be_deleted_but_a_declared_one_cannot()
    {
        var wrong = new SupplierTaxProfile { Id = Guid.NewGuid(), SupplierName = "Uber B.V.", Country = "NL", VatId = "NL852071588B01", ValidFrom = new DateOnly(2025, 1, 1) };
        var used = new SupplierTaxProfile { Id = Guid.NewGuid(), SupplierName = "Bolt Operations OÜ", Country = "EE", VatId = "EE102090374", ValidFrom = new DateOnly(2025, 1, 1) };
        _db.SupplierTaxProfiles.AddRange(wrong, used);
        _db.DeclarationLines.Add(new DeclarationLine { Id = Guid.NewGuid(), DeclarationVersionId = Guid.NewGuid(), SourceDocumentId = Guid.NewGuid(), SupplierVatId = "EE102090374" });
        await _db.SaveChangesAsync();
        var delete = new DeleteSupplierCommandHandler(_db, User());

        (await delete.Handle(new DeleteSupplierCommand(wrong.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await delete.Handle(new DeleteSupplierCommand(used.Id), CancellationToken.None)).Error.Code.ShouldBe("Accounting.SupplierInUse");

        // Șters logic: nu mai apare în registru, dar rândul și auditul rămân.
        IReadOnlyList<object> suppliers = (await new ListTaxRulesQueryHandler(_db).Handle(new ListTaxRulesQuery(TaxRuleKind.Suppliers), CancellationToken.None)).Value;
        suppliers.Cast<SupplierTaxProfileDto>().Select(s => s.VatId).ShouldBe(["EE102090374"]);
        (await _db.SupplierTaxProfiles.IgnoreQueryFilters().SingleAsync(s => s.Id == wrong.Id)).DeletedByUserId.ShouldBe(_accountant);
        (await _db.AuditLogs.AnyAsync(a => a.EntityId == wrong.Id.ToString() && a.Action == "DELETE")).ShouldBeTrue();
    }

    [Fact]
    public async Task Settings_are_appended_with_their_history()
    {
        (await Update(PfaAccountingSettingKeys.Art317, "true", new DateOnly(2026, 1, 1), "Cod primit")).IsSuccess.ShouldBeTrue();
        PfaAccountingSettingsDto settings = (await Update(PfaAccountingSettingKeys.Art317, "false", new DateOnly(2026, 6, 1), "Cod retras")).Value;

        (settings.Art317, settings.VehicleDeductibility, settings.RealSystem, settings.VatPayer).ShouldBe((new Art317Setting(false, null), DeductibilityType.Percent50, true, false));
        settings.History.Select(h => (h.Key, h.Value.GetBoolean(), h.ValidFrom, h.ValidTo, h.ChangedBy.Name)).ShouldBe(
        [
            ("art317", true, new DateOnly(2026, 1, 1), (DateOnly?)new DateOnly(2026, 5, 31), "Contabil RIDElance"),
            ("art317", false, new DateOnly(2026, 6, 1), null, "Contabil RIDElance"),
        ]);
        (await Update(PfaAccountingSettingKeys.Art317, "true", new DateOnly(2026, 6, 1), "Din nou")).Error.Code.ShouldBe("Accounting.SettingExists");
        (await Update(PfaAccountingSettingKeys.Platforms, "[]", new DateOnly(2026, 6, 1), "Nimic")).Error.Code.ShouldBe("Accounting.PlatformsRequired");
        (await Update(PfaAccountingSettingKeys.VehicleDeductibility, "\"NON_DEDUCTIBLE\"", new DateOnly(2026, 6, 1), "X")).Error.Code.ShouldBe("Accounting.InvalidField");
        (await Update("necunoscut", "1", new DateOnly(2026, 6, 1), "X")).Error.Code.ShouldBe("Accounting.InvalidField");
        (await Update(PfaAccountingSettingKeys.Art317, "true", new DateOnly(2026, 7, 1), " ")).Error.Code.ShouldBe("Accounting.ReasonRequired");
        (await new UpdatePfaSettingsCommandHandler(_db, User(), Options.Create(new AccountingOptions()))
            .Handle(new UpdatePfaSettingsCommand(_stefan, new SettingsChange(PfaAccountingSettingKeys.Art317, Json("true"), new DateOnly(2026, 1, 1), "X")), CancellationToken.None))
            .Error.Code.ShouldBe("Accounting.PfaReadOnly");
    }

    [Fact]
    public async Task A_new_vehicle_deductibility_reapplies_to_open_expenses()
    {
        var entry = new LedgerEntry
        {
            Id = Guid.NewGuid(), PfaRegistrationId = _ion, Date = new DateOnly(2026, 8, 10), DocumentLabel = "Extras", Source = LedgerSource.Bank,
            Description = "OMV", TransactionType = LedgerTransactionType.Expense, PaymentMethod = PaymentMethod.Bank, Amount = -200m, Category = "FUEL", AccountingPeriod = "2026-08",
        };
        DeductibilityService.Resolve(entry, new LedgerRules([.. _db.ExpenseCategoryRules], []));
        entry.DeductibleAmount.ShouldBe(100m); // implicit 50%, fără setare
        _db.LedgerEntries.Add(entry);
        await _db.SaveChangesAsync();

        await Update(PfaAccountingSettingKeys.VehicleDeductibility, "\"100_PERCENT\"", new DateOnly(2026, 8, 1), "Autoturism folosit exclusiv pentru curse");

        (await _db.LedgerEntries.SingleAsync()).DeductibleAmount.ShouldBe(200m);
    }

    [Fact]
    public async Task Cash_activation_needs_verification_and_the_fiscal_evidence()
    {
        var files = new DeclarationFiles(_db, new AnafDeclarationXmlService(), new MemoryFiles(), new PlainSecrets());
        var transition = new TransitionCashCommandHandler(_db, User());

        (await transition.Handle(new TransitionCashCommand(_ion, new CashTransitionRequest(CashRegisterStatus.Active, "Direct", null)), CancellationToken.None))
            .Error.Description.ShouldBe("Casa de marcat nu poate trece din NOT_REQUIRED_CURRENT_CONFIGURATION în ACTIVE.");
        (await transition.Handle(new TransitionCashCommand(_ion, new CashTransitionRequest(CashRegisterStatus.InVerification, "Cerere PFA", null)), CancellationToken.None))
            .Value.Status.ShouldBe(CashRegisterStatus.InVerification);
        (await transition.Handle(new TransitionCashCommand(_ion, new CashTransitionRequest(CashRegisterStatus.Active, "Fără dovadă", null)), CancellationToken.None))
            .Error.Code.ShouldBe("Accounting.EvidenceRequired");

        CashEvidenceUploadResult evidence = (await new UploadCashEvidenceCommandHandler(_db, files, User(), Options.Create(new AccountingOptions()))
            .Handle(new UploadCashEvidenceCommand(_ion, new LedgerUpload("fiscalizare.pdf", "application/pdf", [1, 2])), CancellationToken.None)).Value;
        CashRegisterStateDto active = (await transition.Handle(
            new TransitionCashCommand(_ion, new CashTransitionRequest(CashRegisterStatus.Active, "Verificat în SPV", evidence.DocumentId)), CancellationToken.None)).Value;

        (active.Status, active.CashEnabled, active.ActivationDate, active.VerifiedBy!.Name, active.EvidenceFile!.FileName)
            .ShouldBe((CashRegisterStatus.Active, true, (DateOnly?)DateOnly.FromDateTime(DateTime.UtcNow), "Contabil RIDElance", "fiscalizare.pdf"));
        (await _db.AuditLogs.Select(a => a.Action).ToListAsync()).ShouldContain("CASH_ACTIVE");
    }

    [Fact]
    public async Task The_pfa_answers_the_cash_question_in_onboarding()
    {
        var me = new FixedUser(_ionUser);
        (await new GetMyCashPreferenceQueryHandler(_db, me).Handle(new GetMyCashPreferenceQuery(), CancellationToken.None)).Value.ShouldBeNull();

        CashPreference yes = (await new SetMyCashPreferenceCommandHandler(_db, me).Handle(new SetMyCashPreferenceCommand(true), CancellationToken.None)).Value;

        yes.CashRequested.ShouldBeTrue();
        (await _db.CashRegisterStates.SingleAsync()).Status.ShouldBe(CashRegisterStatus.Pending);
        (await new GetMyCashPreferenceQueryHandler(_db, me).Handle(new GetMyCashPreferenceQuery(), CancellationToken.None)).Value!.CashRequested.ShouldBeTrue();

        await new SetMyCashPreferenceCommandHandler(_db, me).Handle(new SetMyCashPreferenceCommand(false), CancellationToken.None);
        (await _db.CashRegisterStates.SingleAsync()).Status.ShouldBe(CashRegisterStatus.NotRequiredCurrentConfiguration);
        (await new SetMyCashPreferenceCommandHandler(_db, new FixedUser(Guid.NewGuid())).Handle(new SetMyCashPreferenceCommand(true), CancellationToken.None))
            .Error.Code.ShouldBe("Accounting.PfaNotFound");
    }

    [Fact]
    public async Task Tax_rules_do_not_overlap_for_the_same_key()
    {
        var save = new SaveTaxRuleCommandHandler(_db);

        var bolt = (SupplierTaxProfileDto)(await save.Handle(new SaveTaxRuleCommand(TaxRuleKind.Suppliers, null, Json("""
            {"supplierName":"Bolt Operations OÜ","country":"ee","vatId":"ee 102090374","incomeType":"COMMISSION","treaty":"Convenția RO–EE","d100Rate":2,
             "d100RateConfirmed":true,"validFrom":"2025-01-01","validTo":null,"residenceCertValidFrom":null,"residenceCertValidTo":null,"residenceCertFile":null,"note":null}
            """)), CancellationToken.None)).Value;
        (bolt.VatId, bolt.Country).ShouldBe(("EE102090374", "EE"));

        Result<object> overlap = await save.Handle(new SaveTaxRuleCommand(TaxRuleKind.Suppliers, null, Json("""
            {"supplierName":"Bolt","country":"EE","vatId":"EE102090374","incomeType":"COMMISSION","d100Rate":1,"d100RateConfirmed":true,"validFrom":"2026-01-01","validTo":null}
            """)), CancellationToken.None);
        overlap.Error.Description.ShouldBe("Pentru EE102090374 există deja o regulă valabilă din 01.01.2025. Închide-o întâi prin „Valabil până la”.");

        (await save.Handle(new SaveTaxRuleCommand(TaxRuleKind.Suppliers, bolt.Id, Json("""
            {"supplierName":"Bolt Operations OÜ","country":"EE","vatId":"EE102090374","incomeType":"COMMISSION","d100Rate":2,"d100RateConfirmed":true,"validFrom":"2025-01-01","validTo":"2025-12-31"}
            """)), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await save.Handle(new SaveTaxRuleCommand(TaxRuleKind.Suppliers, null, Json("""
            {"supplierName":"Bolt","country":"EE","vatId":"EE102090374","incomeType":"COMMISSION","d100Rate":1,"d100RateConfirmed":true,"validFrom":"2026-01-01","validTo":null}
            """)), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        (await save.Handle(new SaveTaxRuleCommand(TaxRuleKind.VatRates, null, Json("""{"rate":21,"validFrom":"2026-01-01","validTo":"2025-01-01"}""")), CancellationToken.None))
            .Error.Code.ShouldBe("Accounting.InvalidValidity");
        (await save.Handle(new SaveTaxRuleCommand(TaxRuleKind.D100, Guid.NewGuid(), Json("""{"code":"D100_RENT_INDIVIDUAL","enabled":false,"description":"x","pendingConfirmation":true,"parameters":{},"validFrom":"2025-01-01","validTo":null}""")), CancellationToken.None))
            .Error.Code.ShouldBe("Accounting.RuleNotFound");
        (await save.Handle(new SaveTaxRuleCommand(TaxRuleKind.ExpenseCategories, null, Json("""{"category":"","label":"x","vehicleRelated":false,"defaultDeductibility":"100_PERCENT","validFrom":"2025-01-01"}""")), CancellationToken.None))
            .Error.Code.ShouldBe("Accounting.InvalidRule");

        IReadOnlyList<object> suppliers = (await new ListTaxRulesQueryHandler(_db).Handle(new ListTaxRulesQuery(TaxRuleKind.Suppliers), CancellationToken.None)).Value;
        suppliers.Cast<SupplierTaxProfileDto>().Select(s => (s.ValidFrom, s.ValidTo)).ShouldBe([(new DateOnly(2025, 1, 1), (DateOnly?)new DateOnly(2025, 12, 31)), (new DateOnly(2026, 1, 1), null)]);
        (await new ListTaxRulesQueryHandler(_db).Handle(new ListTaxRulesQuery(TaxRuleKind.ExpenseCategories), CancellationToken.None)).Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Exchange_rate_is_the_last_one_published_until_the_date()
    {
        _db.ExchangeRates.AddRange(
            new ExchangeRate { Id = Guid.NewGuid(), Currency = "EUR", Date = new DateOnly(2026, 8, 28), Rate = 5.07m, Source = "BNR" },
            new ExchangeRate { Id = Guid.NewGuid(), Currency = "EUR", Date = new DateOnly(2026, 9, 1), Rate = 5.08m, Source = "BNR" });
        await _db.SaveChangesAsync();
        var handler = new GetExchangeRateQueryHandler(_db);

        (await handler.Handle(new GetExchangeRateQuery("eur", new DateOnly(2026, 8, 31)), CancellationToken.None)).Value.ShouldBe(new ExchangeRateDto("EUR", new DateOnly(2026, 8, 28), 5.07m, "BNR"));
        (await handler.Handle(new GetExchangeRateQuery("USD", new DateOnly(2026, 8, 31)), CancellationToken.None)).Value.ShouldBeNull();
    }

    private Task<Result<PfaAccountingSettingsDto>> Update(string field, string value, DateOnly from, string note) =>
        new UpdatePfaSettingsCommandHandler(_db, User(), Options.Create(new AccountingOptions()))
            .Handle(new UpdatePfaSettingsCommand(_ion, new SettingsChange(field, Json(value), from, note)), CancellationToken.None);

    private void Pfa(Guid id, Guid userId, string name, string cui)
    {
        var user = new User { Id = userId, Email = $"{userId:N}@ridelance.ro", FirstName = name.Split(' ')[0], LastName = name.Split(' ')[1] };
        _db.PfaRegistrations.Add(new PfaRegistration { Id = id, UserId = userId, User = user, FullName = name, Cui = cui, OnboardingCompletedAtUtc = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc) });
    }

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private FixedUser User() => new(_accountant);

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
