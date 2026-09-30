using Application.Abstractions.Authentication;
using Application.Accounting.Assets;
using Application.Accounting.Contracts;
using Application.Accounting.FiscalRegister;
using Application.Accounting.Ledger;
using Application.Accounting.Registers;
using Domain.Accounting;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>Spec registre §6: posibilul mijloc fix, decizia Adminului, planul de amortizare, ieșirea din gestiune.</summary>
public sealed class AssetTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly Guid _pfa = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    public AssetTests()
    {
        var user = new User { Id = _user, Email = "ion@ridelance.ro", FirstName = "Ion", LastName = "Popescu" };
        _db.Users.Add(new User { Id = _admin, Email = "admin@ridelance.ro", FirstName = "Admin", LastName = "RIDElance" });
        _db.PfaRegistrations.Add(new PfaRegistration { Id = _pfa, UserId = user.Id, User = user, FullName = "Ion Popescu", LegalName = "POPESCU ION PFA", Cui = "12345674" });
        _db.FixedAssetRules.Add(new FixedAssetRule
        {
            Id = Guid.NewGuid(), Threshold = 2500m, DepreciationStart = DepreciationStart.NextMonth,
            ExcludedCategories = "FUEL|CAR_SERVICE|PLATFORM_COMMISSION", ValidFrom = new DateOnly(2016, 1, 1),
        });
        _db.ExpenseCategoryRules.Add(new ExpenseCategoryRule
        {
            Id = Guid.NewGuid(), Category = "FUEL", Label = "Combustibil", DefaultDeductibility = DeductibilityType.Percent100, ValidFrom = new DateOnly(2025, 1, 1),
        });
        _db.ExpenseCategoryRules.Add(new ExpenseCategoryRule
        {
            Id = Guid.NewGuid(), Category = "IT", Label = "Echipamente IT", DefaultDeductibility = DeductibilityType.Percent100, ValidFrom = new DateOnly(2025, 1, 1),
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>§6 pas 2: peste prag și fără natură de consum → posibil mijloc fix, deductibil 0.</summary>
    [Fact]
    public async Task S4_TaxEngineProposesLargePurchasesAndHoldsTheDeduction()
    {
        LedgerEntry laptop = await Expense(-6000m, "IT", "Laptop");
        LedgerEntry fuel = await Expense(-3000m, "FUEL", "Carburant");
        LedgerEntry mouse = await Expense(-200m, "IT", "Mouse");

        (laptop.FixedAssetReview, laptop.DeductibleAmount).ShouldBe((FixedAssetReview.Pending, (decimal?)0m));
        (fuel.FixedAssetReview, fuel.DeductibleAmount).ShouldBe((FixedAssetReview.None, (decimal?)3000m));
        (mouse.FixedAssetReview, mouse.DeductibleAmount).ShouldBe((FixedAssetReview.None, (decimal?)200m));
        (await new ListFixedAssetCandidatesQueryHandler(_db).Handle(new ListFixedAssetCandidatesQuery(_pfa), CancellationToken.None))
            .Value.ShouldHaveSingleItem().LedgerEntryId.ShouldBe(laptop.Id);
    }

    /// <summary>Scenariile 4–5: laptop 6.000, MF-0001, 36 luni, în funcțiune 01.03.2026 → 166,67 lei/lună din aprilie.</summary>
    [Fact]
    public async Task S5_ALaptopBecomesMf0001WithLinearDepreciationFromTheNextMonth()
    {
        var invoice = new EFacturaMessage
        {
            Id = Guid.NewGuid(), PfaRegistrationId = _pfa, AnafMessageId = "1", InvoiceNumber = "LPT-77", IssueDate = new DateOnly(2026, 2, 10),
            SupplierName = "Altex Romania SRL", TotalAmount = 6000m,
        };
        _db.EFacturaMessages.Add(invoice);
        LedgerEntry laptop = await Expense(-6000m, "IT", "Laptop", new DateOnly(2026, 2, 12), invoice.Id);

        AssetDto created = (await Decide(laptop.Id, FixedAssetReview.FixedAsset, "Laptop Lenovo")).Value!;
        (created.InventoryNumber, created.Status, created.DocumentRef, created.SupplierName, created.EntryDate, created.EntryValue)
            .ShouldBe(("MF-0001", AssetStatus.PendingClassification, "Factura LPT-77 / 10.02.2026", (string?)"Altex Romania SRL", new DateOnly(2026, 2, 10), 6000m));
        (await _db.LedgerEntries.SingleAsync(e => e.Id == laptop.Id)).DeductibleAmount.ShouldBe(0m);

        AssetDto active = (await Classify(created.Id, new DateOnly(2026, 3, 1), "2.2.9", 36)).Value;
        active.Status.ShouldBe(AssetStatus.Active);
        active.MonthlyDepreciation.ShouldBe(166.67m);

        List<DepreciationLine> lines = [.. _db.DepreciationLines.OrderBy(l => l.Year).ThenBy(l => l.Month)];
        lines.Count.ShouldBe(36);
        (lines[0].Year, lines[0].Month).ShouldBe((2026, 4));
        lines.Where(l => l.Year == 2026).Sum(l => l.Amount).ShouldBe(1500.03m);
        lines.Single(l => l.Year == 2026 && l.Month == 12).Remaining.ShouldBe(4499.97m);
        (lines[^1].Year, lines[^1].Month, lines[^1].Amount, lines[^1].Remaining).ShouldBe((2029, 3, 166.55m, 0m));

        AssetDto yearEnd = (await new ListAssetsQueryHandler(_db).Handle(new ListAssetsQuery(_pfa, new DateOnly(2026, 12, 31)), CancellationToken.None)).Value.Single();
        (yearEnd.Accumulated, yearEnd.Remaining).ShouldBe((1500.03m, 4499.97m));

        // Scenariul 4: plata de 6.000 nu se deduce; în REF intră doar amortizarea anului.
        RefView refView = (await new GetRefQueryHandler(_db).Handle(new GetRefQuery(_pfa, 2026), CancellationToken.None)).Value;
        RefRow expenses = refView.Rows.Single(r => r.CalculationElement == "Cheltuieli deductibile");
        expenses.Value.ShouldBe(1500.03m);
        expenses.Contributions!.ShouldAllBe(c => c.AssetId == created.Id && c.LedgerEntryId == null);
        expenses.Contributions!.Count.ShouldBe(9);
    }

    /// <summary>§6: Fișa MF (14-2-2) cu planul lunar și lista activelor; PFA-ul își vede doar activele lui.</summary>
    [Fact]
    public async Task S4_TheFixedAssetSheetAndTheAssetListAreGenerated()
    {
        AssetDto asset = await ActiveLaptop();
        var exporter = new Infrastructure.Accounting.RegisterExporter();

        RegisterFile sheet = (await new ExportAssetSheetQueryHandler(_db, exporter)
            .Handle(new ExportAssetSheetQuery(_pfa, asset.Id, RegisterFormat.Xlsx), CancellationToken.None)).Value;
        sheet.FileName.ShouldBe("Fisa_MF_MF-0001_12345674.xlsx");
        using (var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(sheet.Content)))
        {
            var rows = workbook.Worksheet(1).RowsUsed().ToList();
            rows.ShouldContain(r => r.Cell(1).GetString() == "Nr. inventar MF-0001 — Laptop Lenovo");
            ClosedXML.Excel.IXLRow first = rows.First(r => r.Cell(2).GetString() == "04.2026");
            (first.Cell(3).GetValue<decimal>(), first.Cell(5).GetValue<decimal>()).ShouldBe((166.67m, 5833.33m));
            rows.Count(r => r.Cell(2).GetString().EndsWith(".2029", StringComparison.Ordinal)).ShouldBe(3);
        }

        RegisterFile list = (await new ExportAssetListQueryHandler(_db, exporter)
            .Handle(new ExportAssetListQuery(_pfa, new DateOnly(2026, 12, 31), RegisterFormat.Csv), CancellationToken.None)).Value;
        System.Text.Encoding.UTF8.GetString(list.Content).ShouldContain("MF-0001;Laptop Lenovo;Mijloc fix;12.02.2026;01.03.2026;6000,00;36;1500,03;4499,97;Activ");

        (await new GetClientAssetsQueryHandler(_db, new FixedUser(_user)).Handle(new GetClientAssetsQuery(), CancellationToken.None)).Value.ShouldHaveSingleItem();
        (await new GetClientAssetSheetQueryHandler(_db, new FixedUser(Guid.NewGuid()), new ExportAssetSheetQueryHandler(_db, exporter))
            .Handle(new GetClientAssetSheetQuery(asset.Id), CancellationToken.None)).Error.Code.ShouldBe("Accounting.NoPfa");
    }

    /// <summary>§6 pas 3: „cheltuială curentă” deduce plata după categorie; decizia nu se ia de două ori.</summary>
    [Fact]
    public async Task S4_AnExpenseDecisionDeductsByCategoryAndIsFinal()
    {
        LedgerEntry printer = await Expense(-2800m, "IT", "Imprimantă");

        (await Decide(printer.Id, FixedAssetReview.Expense, null)).Value.ShouldBeNull();

        LedgerEntry decided = await _db.LedgerEntries.SingleAsync(e => e.Id == printer.Id);
        (decided.FixedAssetReview, decided.DeductibleAmount).ShouldBe((FixedAssetReview.Expense, (decimal?)2800m));
        (await Decide(printer.Id, FixedAssetReview.FixedAsset, null)).Error.Code.ShouldBe("Accounting.FixedAssetDecided");
        (await _db.PfaAssets.CountAsync()).ShouldBe(0);
        (await _db.AuditLogs.SingleAsync(a => a.Action == "FIXED_ASSET_DECISION")).EntityId.ShouldBe(printer.Id.ToString());
    }

    /// <summary>Scenariul 8: mașina fără decizia Adminului nu e activ.</summary>
    [Fact]
    public async Task S8_ACarWithoutAnAdminDecisionIsNotAnAsset()
    {
        await Expense(-45000m, null, "Autoturism Dacia");

        (await new ListAssetsQueryHandler(_db).Handle(new ListAssetsQuery(_pfa), CancellationToken.None)).Value.ShouldBeEmpty();
    }

    /// <summary>§6: ieșirea din gestiune oprește amortizarea din luna următoare; lunile închise rămân.</summary>
    [Fact]
    public async Task Disposal_StopsDepreciationAfterItsMonthAndKeepsClosedMonths()
    {
        AssetDto asset = await ActiveLaptop();
        foreach (DepreciationLine line in _db.DepreciationLines.Where(l => l.Year == 2026 && l.Month <= 5))
        {
            line.IsLocked = true;
        }

        await _db.SaveChangesAsync();

        (await Dispose(asset.Id, new DateOnly(2026, 4, 20))).Error.Code.ShouldBe("Accounting.AssetDisposalInClosedMonth");
        _db.ChangeTracker.Clear();
        AssetDto disposed = (await Dispose(asset.Id, new DateOnly(2026, 7, 15))).Value;

        disposed.Status.ShouldBe(AssetStatus.Disposed);
        (await _db.DepreciationLines.Select(l => l.Month).OrderBy(m => m).ToListAsync()).ShouldBe([4, 5, 6, 7]);
    }

    /// <summary>§6: o modificare recalculează doar lunile deschise, în continuarea celor închise.</summary>
    [Fact]
    public async Task Reclassification_RecalculatesOnlyOpenMonths()
    {
        AssetDto asset = await ActiveLaptop();
        foreach (DepreciationLine line in _db.DepreciationLines.Where(l => l.Year == 2026 && l.Month <= 6))
        {
            line.IsLocked = true;
        }

        await _db.SaveChangesAsync();

        await Classify(asset.Id, new DateOnly(2026, 3, 1), "2.2.9", 24);

        List<DepreciationLine> lines = [.. _db.DepreciationLines.OrderBy(l => l.Year).ThenBy(l => l.Month)];
        lines.Count.ShouldBe(24);
        lines.Take(3).ShouldAllBe(l => l.IsLocked && l.Amount == 166.67m);
        lines[3].Amount.ShouldBe(LedgerInvariants_Round((6000m - 500.01m) / 21));
        lines[^1].Remaining.ShouldBe(0m);
    }

    /// <summary>§6 pas 4: activul fără plată (aport) și numerotarea separată a obiectelor de inventar.</summary>
    [Fact]
    public async Task Manual_AssetsGetTheirOwnSequencePerKind()
    {
        var handler = new CreateManualAssetCommandHandler(_db, new FixedUser(_admin));
        (await handler.Handle(new CreateManualAssetCommand(_pfa, new ManualAssetRequest("", AssetKind.FixedAsset, new DateOnly(2026, 1, 5), 10m, "PV", null, null)), CancellationToken.None))
            .Error.Code.ShouldBe("Accounting.AssetInvalid");

        AssetDto chair = (await handler.Handle(
            new CreateManualAssetCommand(_pfa, new ManualAssetRequest("Scaun auto copil", AssetKind.InventoryObject, new DateOnly(2026, 1, 5), 900m, "PV aport 1", null, "Aport")),
            CancellationToken.None)).Value;
        AssetDto car = (await handler.Handle(
            new CreateManualAssetCommand(_pfa, new ManualAssetRequest("Dacia Logan", AssetKind.FixedAsset, new DateOnly(2026, 1, 5), 40000m, "PV aport 2", null, "Aport")),
            CancellationToken.None)).Value;

        (chair.InventoryNumber, car.InventoryNumber).ShouldBe(("OI-0001", "MF-0001"));
        (await Classify(chair.Id, new DateOnly(2026, 1, 5), null, null)).Value.Status.ShouldBe(AssetStatus.Active);
        _db.DepreciationLines.ShouldBeEmpty();
    }

    /// <summary>§6: un an închis nu mai primește linii de amortizare.</summary>
    [Fact]
    public async Task Classification_IntoAClosedYearIsRefused()
    {
        LedgerEntry laptop = await Expense(-6000m, "IT", "Laptop", new DateOnly(2025, 2, 12));
        AssetDto asset = (await Decide(laptop.Id, FixedAssetReview.FixedAsset, null)).Value!;
        _db.AccountingYears.Add(new AccountingYear { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Year = 2025, Status = AccountingPeriodStatus.Closed });
        await _db.SaveChangesAsync();

        (await Classify(asset.Id, new DateOnly(2025, 3, 1), "2.2.9", 36)).Error.Code.ShouldBe("Accounting.YearClosed");
    }

    // ─── Ajutoare ──────────────────────────────────────────────────────────────────────────────

    private static decimal LedgerInvariants_Round(decimal value) => LedgerInvariants.Round(value);

    private async Task<AssetDto> ActiveLaptop()
    {
        LedgerEntry laptop = await Expense(-6000m, "IT", "Laptop", new DateOnly(2026, 2, 12));
        AssetDto created = (await Decide(laptop.Id, FixedAssetReview.FixedAsset, null)).Value!;
        return (await Classify(created.Id, new DateOnly(2026, 3, 1), "2.2.9", 36)).Value;
    }

    private async Task<LedgerEntry> Expense(decimal amount, string? category, string description, DateOnly? date = null, Guid? invoice = null)
    {
        DateOnly day = date ?? new DateOnly(2026, 2, 12);
        var entry = new LedgerEntry
        {
            Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Date = day, DocumentLabel = "Extras", Source = LedgerSource.Bank,
            Description = description, TransactionType = LedgerTransactionType.Expense, PaymentMethod = PaymentMethod.Bank, Amount = amount,
            Category = category, EFacturaMessageId = invoice, Status = LedgerEntryStatus.Verified, AccountingPeriod = LedgerSupport.PeriodOf(day),
        };
        DeductibilityService.Resolve(entry, await LedgerSupport.RulesAsync(_db, _pfa, CancellationToken.None));
        _db.LedgerEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    private Task<Result<AssetDto?>> Decide(Guid entryId, FixedAssetReview decision, string? name) =>
        new DecideFixedAssetCommandHandler(_db, new FixedUser(_admin))
            .Handle(new DecideFixedAssetCommand(_pfa, entryId, decision, name, "Decizie contabil"), CancellationToken.None);

    private Task<Result<AssetDto>> Classify(Guid assetId, DateOnly inService, string? classCode, int? life) =>
        new ClassifyAssetCommandHandler(_db, new FixedUser(_admin))
            .Handle(new ClassifyAssetCommand(_pfa, assetId, new AssetClassificationRequest("Laptop Lenovo", "Factura LPT-77", null, inService, classCode, life, "Clasificare")), CancellationToken.None);

    private Task<Result<AssetDto>> Dispose(Guid assetId, DateOnly date) =>
        new DisposeAssetCommandHandler(_db, new FixedUser(_admin))
            .Handle(new DisposeAssetCommand(_pfa, assetId, new AssetDisposalRequest(date, "Vândut")), CancellationToken.None);

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
