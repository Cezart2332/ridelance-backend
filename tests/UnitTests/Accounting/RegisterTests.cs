using System.Text;
using Application.Accounting.Contracts;
using Application.Accounting.FiscalRegister;
using Application.Accounting.Inventory;
using Application.Accounting.Registers;
using ClosedXML.Excel;
using Application.Accounting;
using Domain.Accounting;
using Microsoft.Extensions.Options;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Accounting;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>B7: registrele ca proiecții din ledger — RJIP, REF, Registrul-inventar — și exporturile lor.</summary>
public sealed class RegisterTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 10);

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly Guid _pfa = Guid.NewGuid();
    private readonly RegisterExporter _exporter = new();

    public RegisterTests()
    {
        _db.ExpenseCategoryRules.Add(new ExpenseCategoryRule
        {
            Id = Guid.NewGuid(), Category = "FUEL", Label = "Combustibil", VehicleRelated = true,
            DefaultDeductibility = DeductibilityType.Percent100, ValidFrom = new DateOnly(2025, 1, 1),
        });
        // Ca în DependencyInjection: licența Community se setează la pornirea aplicației.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var user = new User { Id = Guid.NewGuid(), Email = "ion@ridelance.ro", FirstName = "Ion", LastName = "Popescu" };
        _db.PfaRegistrations.Add(new PfaRegistration { Id = _pfa, UserId = user.Id, User = user, FullName = "Ion Popescu", LegalName = "POPESCU ION PFA", Cui = "12345674" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Spec §5.3: o zi în RIDElance apare corect în RJIP și REF.</summary>
    [Fact]
    public async Task A_day_in_ridelance_in_rjip_and_ref()
    {
        ADayInRidelance();

        RjipView rjip = await Rjip(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        RefView refView = await Ref(2026);

        rjip.Rows.Select(r => (r.No, r.Document.Split(", ref. ")[0], r.CashIn, r.CashOut, r.BankIn, r.BankOut)).ShouldBe(
        [
            (1, "Extras bancar", 0m, 0m, 0m, 300m),
            (2, "Extras bancar", 0m, 0m, 1850m, 0m),
            (3, "Raport Z nr. 125", 420m, 0m, 0m, 0m),
        ]);
        rjip.Rows.Take(2).ShouldAllBe(r => r.Document.StartsWith("Extras bancar, ref. ", StringComparison.Ordinal));
        rjip.Rows.Select(r => r.Operation).ShouldBe(["Combustibil", "Încasare venit din activitate", "Încasări numerar, raport Z nr. 125"]);
        rjip.MonthTotals.ShouldBe([new RjipMonthTotal("2026-10", 420m, 0m, 1850m, 300m)]);

        (refView.Status, refView.AsOf).ShouldBe((RefStatus.Current, (DateOnly?)null));
        refView.Rows.Select(r => (r.CalculationElement, r.Value)).ShouldBe([("Venit brut", 2270m), ("Cheltuieli deductibile", 300m), ("Venit net anual", 1970m)]);
        refView.Rows.ShouldAllBe(r => r.Year == 2026 && !r.Rectification && r.IncomeCategory == GetRefQueryHandler.IncomeCategory);
    }

    /// <summary>RJIP are suma plătită, REF suma deductibilă (spec B7: 1.000 plătiți, 500 deductibili la 50%).</summary>
    [Fact]
    public async Task Rjip_shows_what_was_paid_and_ref_what_is_deductible()
    {
        Entry(new DateOnly(2027, 3, 15), -1000m, LedgerTransactionType.Expense, PaymentMethod.Bank, "Revizie", deductible: 500m);
        await _db.SaveChangesAsync();

        (await Rjip(new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31))).Rows.ShouldHaveSingleItem().BankOut.ShouldBe(1000m);
        (await Ref(2027)).Rows.Single(r => r.CalculationElement == "Cheltuieli deductibile").Value.ShouldBe(500m);
    }

    /// <summary>
    /// Spec flux contabil §7: în registre intră doar încasările și plățile efective. Payout-ul
    /// nereconciliat nu există încă (R20, R22), venitul brut luat doar din raport nu e bani mișcați,
    /// iar aportul și retragerea titularului sunt în RJIP, dar nu în REF (R40, R41).
    /// </summary>
    [Fact]
    public async Task R20_R21_R40_R41_RegistersCountOnlyReconciledMoneyMovements()
    {
        Entry(Day, -300m, LedgerTransactionType.Expense, PaymentMethod.Bank, "Plată în lună închisă", deductible: 300m).ClosedPeriodFlag = true;
        LedgerEntry reportOnly = Entry(new DateOnly(2026, 8, 31), 2500m, LedgerTransactionType.Income, PaymentMethod.Bank, "Venit brut din raport, fără plată");
        reportOnly.BankTransactionId = null;
        reportOnly.PlatformDocumentId = Guid.NewGuid();
        LedgerEntry pending = Entry(new DateOnly(2026, 9, 9), 1850m, LedgerTransactionType.PlatformSettlement, PaymentMethod.Bank, "Payout Uber");
        pending.ReconciliationStatus = ReconciliationStatus.NeedsReconciliation;

        var group = Guid.NewGuid();
        LedgerEntry gross = Entry(new DateOnly(2026, 9, 2), 5000m, LedgerTransactionType.Income, PaymentMethod.Bank, "Venit brut din curse Bolt", counterparty: "Bolt");
        LedgerEntry commission = Entry(new DateOnly(2026, 9, 2), -350m, LedgerTransactionType.Expense, PaymentMethod.Bank, "Comision Bolt", deductible: 350m, counterparty: "Bolt");
        commission.BankTransactionId = gross.BankTransactionId;
        gross.SettlementGroupId = commission.SettlementGroupId = group;

        Entry(new DateOnly(2026, 9, 20), -2000m, LedgerTransactionType.OwnerWithdrawal, PaymentMethod.Bank, "Transfer către titular", counterparty: null);
        Entry(new DateOnly(2026, 9, 21), 2000m, LedgerTransactionType.OwnerContribution, PaymentMethod.Bank, "Aport titular", counterparty: null);
        await _db.SaveChangesAsync();

        RjipView rjip = await Rjip(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        RefView refView = await Ref(2026);

        rjip.Rows.Select(r => (r.BankIn, r.BankOut)).ShouldBe([(5000m, 0m), (0m, 350m), (0m, 2000m), (2000m, 0m)]);
        refView.Rows.Select(r => r.Value).ShouldBe([5000m, 350m, 4650m]);
    }

    /// <summary>R01: o cheltuială fără document apare în RJIP (banii au plecat), dar nu se deduce în REF.</summary>
    [Fact]
    public async Task R01_UndocumentedExpense_IsInRjipButNotDeducted()
    {
        Entry(Day, -300m, LedgerTransactionType.Expense, PaymentMethod.Bank, "OMV", deductible: 300m).ReconciliationStatus = ReconciliationStatus.Unmatched;
        Entry(Day, -100m, LedgerTransactionType.Expense, PaymentMethod.Bank, "Orange", deductible: 100m);
        await _db.SaveChangesAsync();

        (await Rjip(Day, Day)).Rows.Sum(r => r.BankOut).ShouldBe(400m);
        (await Ref(2026)).Rows.Single(r => r.CalculationElement == "Cheltuieli deductibile").Value.ShouldBe(100m);
    }

    /// <summary>Q1: plata cu card neconectat, după configurare.</summary>
    [Theory]
    [InlineData(ManualChannelMapping.OwnerContributionAndCash, 40, 40, 0)]
    [InlineData(ManualChannelMapping.Cash, 0, 40, 0)]
    [InlineData(ManualChannelMapping.Bank, 0, 0, 40)]
    public async Task Q1_CardNotConnected_FollowsTheConfiguredMapping(ManualChannelMapping mapping, int cashIn, int cashOut, int bankOut)
    {
        Entry(Day, -40m, LedgerTransactionType.Expense, PaymentMethod.Manual, "Spălătorie");
        await _db.SaveChangesAsync();

        RjipView rjip = (await new GetRjipQueryHandler(_db, Options.Create(new AccountingOptions { ManualChannelMapping = mapping }))
            .Handle(new GetRjipQuery(_pfa, Day, Day), CancellationToken.None)).Value;

        (rjip.MonthTotals.Single().CashIn, rjip.MonthTotals.Single().CashOut, rjip.MonthTotals.Single().BankOut).ShouldBe(((decimal)cashIn, (decimal)cashOut, (decimal)bankOut));
    }

    [Fact]
    public async Task Foreign_amounts_use_the_bnr_rate_of_the_previous_banking_day()
    {
        _db.ExchangeRates.AddRange(
            new ExchangeRate { Id = Guid.NewGuid(), Currency = "EUR", Date = new DateOnly(2026, 10, 9), Rate = 5.0m, Source = "BNR" },
            new ExchangeRate { Id = Guid.NewGuid(), Currency = "EUR", Date = Day, Rate = 5.1m, Source = "BNR" });
        LedgerEntry eur = Entry(Day, -100m, LedgerTransactionType.Expense, PaymentMethod.Bank, "Abonament", deductible: 100m);
        eur.Currency = "EUR";
        await _db.SaveChangesAsync();

        RjipRow row = (await Rjip(Day, Day)).Rows.ShouldHaveSingleItem();

        row.BankOut.ShouldBe(500m);
        row.Operation.ShouldBe("Cheltuială neclasificată (100,00 EUR × 5 (BNR 09.10.2026))");
        (await Ref(2026)).Rows[1].Value.ShouldBe(500m);
    }

    /// <summary>§4: REF-ul e provizoriu până la închiderea anului; apoi e snapshot-ul închiderii.</summary>
    [Fact]
    public async Task Ref_IsFinalOnlyAfterTheYearIsClosed()
    {
        ADayInRidelance();
        Close("2026-10", "2026-11", "2026-12");
        await _db.SaveChangesAsync();
        (await Ref(2026)).Status.ShouldBe(RefStatus.Current);

        RefView closing = await Ref(2026);
        _db.AccountingYears.Add(new AccountingYear
        {
            Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Year = 2026, Status = AccountingPeriodStatus.Closed,
            RefJson = AccountingJson.Serialize(closing),
        });
        Entry(Day, 999m, LedgerTransactionType.Income, PaymentMethod.Cash, "Adăugat după închidere");
        await _db.SaveChangesAsync();

        RefView final = await Ref(2026);
        (final.Status, final.Rows[0].Value).ShouldBe((RefStatus.Final, 2270m));
    }

    /// <summary>Scenariile 1–3: bonul mixt, transferul către titular și decontarea Bolt, în RJIP și REF.</summary>
    [Fact]
    public async Task Ref_ScenariosMixedReceiptOwnerTransferAndBoltSettlement()
    {
        LedgerEntry receipt = Entry(new DateOnly(2027, 9, 3), -250m, LedgerTransactionType.Expense, PaymentMethod.Bank, "Carburant", deductible: 200m);
        receipt.PersonalAmount = 50m;
        Entry(new DateOnly(2027, 9, 4), -2000m, LedgerTransactionType.OwnerWithdrawal, PaymentMethod.Bank, "Transfer personal", counterparty: "Popescu Ion");
        var group = Guid.NewGuid();
        Entry(new DateOnly(2027, 9, 2), 5000m, LedgerTransactionType.Income, PaymentMethod.Bank, "Venit curse Bolt online (decontare)", counterparty: "Bolt").SettlementGroupId = group;
        Entry(new DateOnly(2027, 9, 2), -350m, LedgerTransactionType.Expense, PaymentMethod.Bank, "Comision Bolt", deductible: 350m, counterparty: "Bolt").SettlementGroupId = group;
        await _db.SaveChangesAsync();

        RjipView rjip = await Rjip(new DateOnly(2027, 9, 1), new DateOnly(2027, 9, 30));
        rjip.MonthTotals.ShouldHaveSingleItem().ShouldBe(new RjipMonthTotal("2027-09", 0m, 0m, 5000m, 2600m));

        RefView refView = await Ref(2027);
        refView.Rows.Select(r => (r.CalculationElement, r.Value)).ShouldBe([("Venit brut", 5000m), ("Cheltuieli deductibile", 550m), ("Venit net anual", 4450m)]);
        refView.Rows[1].Contributions!.Select(c => (c.LedgerEntryId, c.Value)).ShouldContain(((Guid?)receipt.Id, 200m));
        refView.Rows[1].Contributions!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Ref_is_intermediate_at_a_date_or_at_the_end_of_the_engagement()
    {
        ADayInRidelance();
        Entry(new DateOnly(2026, 11, 20), 100m, LedgerTransactionType.Income, PaymentMethod.Cash, "După data cerută");
        await _db.SaveChangesAsync();

        RefView asked = (await new GetRefQueryHandler(_db).Handle(new GetRefQuery(_pfa, 2026, new DateOnly(2026, 10, 31)), CancellationToken.None)).Value;
        (asked.Status, asked.AsOf, asked.Rows[0].Value).ShouldBe((RefStatus.Intermediate, (DateOnly?)new DateOnly(2026, 10, 31), 2270m));

        _db.PfaAccountingEngagements.Add(new PfaAccountingEngagement
        {
            Id = Guid.NewGuid(), PfaRegistrationId = _pfa, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 11, 30), Status = EngagementStatus.Inactive,
        });
        await _db.SaveChangesAsync();
        RefView ended = await Ref(2026);
        (ended.Status, ended.AsOf).ShouldBe((RefStatus.Intermediate, (DateOnly?)new DateOnly(2026, 11, 30)));
    }

    [Fact]
    public async Task A_loss_is_shown_as_a_net_annual_loss()
    {
        Entry(Day, -800m, LedgerTransactionType.Expense, PaymentMethod.Bank, "Service", deductible: 800m);
        Entry(Day, 300m, LedgerTransactionType.Income, PaymentMethod.Cash, "Curse");
        await _db.SaveChangesAsync();

        (await Ref(2026)).Rows[2].ShouldBe(new RefRow(2026, false, GetRefQueryHandler.IncomeCategory, "Pierdere netă anuală", 500m));
    }

    /// <summary>§3: la bancă, documentul e „Extras bancar” și justificativul trece în explicații.</summary>
    [Fact]
    public async Task Rjip_BankRowsShowTheStatementAndTheSupportingDocumentInTheExplanation()
    {
        LedgerEntry fuel = Entry(new DateOnly(2026, 9, 3), -250m, LedgerTransactionType.Expense, PaymentMethod.Bank, "Carburant", deductible: 200m);
        fuel.DocumentLabel = "Bon fiscal 381";
        fuel.Category = "FUEL";
        await _db.SaveChangesAsync();

        RjipRow row = (await Rjip(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30))).Rows.ShouldHaveSingleItem();

        (row.Document.StartsWith("Extras bancar, ref. ", StringComparison.Ordinal), row.Operation, row.BankOut).ShouldBe((true, "Combustibil, bon fiscal 381", 250m));
    }

    /// <summary>§3: exportul CSV, cu totalul fiecărei luni și al anului.</summary>
    [Fact]
    public async Task Rjip_ExportsAYearAsCsvWithMonthlyAndYearTotals()
    {
        ADayInRidelance();
        Entry(new DateOnly(2026, 11, 5), -100m, LedgerTransactionType.Expense, PaymentMethod.Cash, "Spălătorie");
        await _db.SaveChangesAsync();

        RegisterFile csv = (await new ExportRjipQueryHandler(_db, new GetRjipQueryHandler(_db), _exporter)
            .Handle(new ExportRjipQuery(_pfa, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), RegisterFormat.Csv), CancellationToken.None)).Value;

        (csv.FileName, csv.ContentType).ShouldBe(("RJIP_12345674_20260101_20261231.csv", "text/csv"));
        string[] lines = Encoding.UTF8.GetString(csv.Content).TrimStart('\uFEFF').Split("\r\n");
        lines.ShouldContain("Anul 2026");
        lines.ShouldContain(";;;Total octombrie 2026;420,00;1850,00;0,00;300,00");
        lines.ShouldContain(";;;Total noiembrie 2026;0,00;0,00;100,00;0,00");
        lines.ShouldContain(";;;Total Anul 2026;420,00;1850,00;100,00;300,00");

        // Model 14-1-1/b: Nr. crt. continuu pe lună, documentul cu felul și numărul; fără textul băncii.
        lines.ShouldContain(line => line.StartsWith("1;10.10.2026;Extras bancar, ref. ", StringComparison.Ordinal) && line.Contains(";Combustibil;", StringComparison.Ordinal));
        lines.ShouldContain(line => line.StartsWith("3;10.10.2026;Raport Z nr. 125;", StringComparison.Ordinal));
        lines.ShouldContain(line => line.StartsWith("1;05.11.2026;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rjip_export_follows_the_official_model()
    {
        ADayInRidelance();

        RegisterFile pdf = (await new ExportRjipQueryHandler(_db, new GetRjipQueryHandler(_db), _exporter)
            .Handle(new ExportRjipQuery(_pfa, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), RegisterFormat.Pdf), CancellationToken.None)).Value;
        RegisterFile xlsx = (await new ExportRjipQueryHandler(_db, new GetRjipQueryHandler(_db), _exporter)
            .Handle(new ExportRjipQuery(_pfa, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), RegisterFormat.Xlsx), CancellationToken.None)).Value;

        (pdf.FileName, pdf.ContentType).ShouldBe(("RJIP_12345674_20261001_20261031.pdf", "application/pdf"));
        Encoding.ASCII.GetString(pdf.Content, 0, 4).ShouldBe("%PDF");
        xlsx.FileName.ShouldBe("RJIP_12345674_20261001_20261031.xlsx");

        using var workbook = new XLWorkbook(new MemoryStream(xlsx.Content));
        IXLWorksheet sheet = workbook.Worksheet(1);
        sheet.Cell(1, 1).GetString().ShouldBe("REGISTRUL-JURNAL DE ÎNCASĂRI ȘI PLĂȚI");
        sheet.Cell(2, 1).GetString().ShouldBe("POPESCU ION PFA — CUI 12345674");
        IXLRow header = sheet.RowsUsed().First(r => r.Cell(1).GetString() == "Nr. crt.");
        header.Cells(1, 8).Select(c => c.GetString()).ShouldBe(
            ["Nr. crt.", "Data operațiunii de încasare/plată", "Documentul (fel, număr)", "Explicații", "Încasări — Numerar", "Încasări — Bancă", "Plăți — Numerar", "Plăți — Bancă"]);
        IXLRow total = sheet.RowsUsed().Single(r => r.Cell(4).GetString() == "Total octombrie 2026");
        (total.Cell(5).GetValue<decimal>(), total.Cell(6).GetValue<decimal>(), total.Cell(8).GetValue<decimal>()).ShouldBe((420m, 1850m, 300m));
        sheet.RowsUsed().Any(r => r.Cell(1).GetString() == "14-1-1/b").ShouldBeTrue();
    }

    [Fact]
    public async Task Ref_and_inventory_exports()
    {
        ADayInRidelance();
        _db.PfaAssets.AddRange(
            Asset("Casă de marcat", "Datecs DP-25X", new DateOnly(2026, 8, 20), 1350m),
            Asset("Laptop", "Vândut", new DateOnly(2025, 3, 1), 3000m, disposed: new DateOnly(2026, 6, 1)));
        await _db.SaveChangesAsync();

        RegisterFile refFile = (await new ExportRefQueryHandler(_db, new GetRefQueryHandler(_db), _exporter)
            .Handle(new ExportRefQuery(_pfa, 2026, RegisterFormat.Xlsx, null), CancellationToken.None)).Value;
        using (var workbook = new XLWorkbook(new MemoryStream(refFile.Content)))
        {
            IXLWorksheet sheet = workbook.Worksheet(1);
            sheet.Cell(1, 1).GetString().ShouldBe("REGISTRUL DE EVIDENȚĂ FISCALĂ");
            sheet.RowsUsed().Select(r => r.Cell(2).GetString()).ShouldContain("Venit net anual");
        }

        RegisterFile inventory = (await new ExportInventoryQueryHandler(_db, _exporter)
            .Handle(new ExportInventoryQuery(_pfa, 2026, RegisterFormat.Xlsx), CancellationToken.None)).Value;
        inventory.FileName.ShouldBe("Registru-inventar_12345674_20261231.xlsx");
        using (var workbook = new XLWorkbook(new MemoryStream(inventory.Content)))
        {
            IXLWorksheet sheet = workbook.Worksheet(1);
            sheet.Cell(3, 1).GetString().ShouldBe("la data de 31.12.2026 (sfârșitul anului)");
            // Fără inventariere, precompletarea: activul ieșit în an nu mai e, numerarul e cel din registru.
            sheet.RowsUsed().Single(r => r.Cell(2).GetString().StartsWith("OI-0001 Casă de marcat — Datecs DP-25X", StringComparison.Ordinal))
                .Cell(3).GetValue<decimal>().ShouldBe(1350m);
            sheet.RowsUsed().Single(r => r.Cell(2).GetString() == "Total Numerar").Cell(3).GetValue<decimal>().ShouldBe(420m);
            sheet.RowsUsed().Any(r => r.Cell(1).GetString() == "Situație precompletată din sistem, neconfirmată prin inventariere.").ShouldBeTrue();
        }

        Encoding.ASCII.GetString((await new ExportInventoryQueryHandler(_db, _exporter)
            .Handle(new ExportInventoryQuery(_pfa, 2026, RegisterFormat.Pdf), CancellationToken.None)).Value.Content, 0, 4).ShouldBe("%PDF");
    }

    [Fact]
    public async Task Invalid_ranges_and_years_are_refused()
    {
        (await new GetRjipQueryHandler(_db).Handle(new GetRjipQuery(_pfa, Day, Day.AddDays(-1)), CancellationToken.None)).Error.Code.ShouldBe("Accounting.InvalidRange");
        (await new GetRefQueryHandler(_db).Handle(new GetRefQuery(_pfa, 2026, new DateOnly(2025, 5, 1)), CancellationToken.None)).Error.Code.ShouldBe("Accounting.InvalidYear");
        (await new ExportInventoryQueryHandler(_db, _exporter).Handle(new ExportInventoryQuery(Guid.NewGuid(), 2026, RegisterFormat.Pdf), CancellationToken.None))
            .Error.Code.ShouldBe("Accounting.PfaNotFound");
    }

    // ─── Ajutoare ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Spec §5.3, cum îl lasă ledger-ul după import (B6).</summary>
    private void ADayInRidelance()
    {
        Entry(Day, -300m, LedgerTransactionType.Expense, PaymentMethod.Bank, "Plată combustibil", deductible: 300m).Category = "FUEL";
        Entry(Day, 1850m, LedgerTransactionType.Income, PaymentMethod.Bank, "Payout Bolt", counterparty: "BOLT OPERATIONS OU");
        LedgerEntry z = Entry(Day, 420m, LedgerTransactionType.Income, PaymentMethod.Cash, "Încasări numerar, raport Z nr. 125", counterparty: null);
        z.DocumentLabel = "Raport Z nr. 125";
        z.BankTransactionId = null;
        z.Source = LedgerSource.CashZ;
        _db.SaveChanges();
    }

    private int _order;

    private LedgerEntry Entry(
        DateOnly date,
        decimal amount,
        LedgerTransactionType type,
        PaymentMethod method,
        string description,
        decimal? deductible = null,
        string? counterparty = "OMV Petrom")
    {
        var entry = new LedgerEntry
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = _pfa,
            Date = date,
            DocumentLabel = $"Extras {date:dd.MM.yyyy}",
            Source = LedgerSource.Bank,
            BankTransactionId = method == PaymentMethod.Bank ? Guid.NewGuid() : null,
            Counterparty = counterparty,
            Description = description,
            TransactionType = type,
            PaymentMethod = method,
            Amount = amount,
            DeductibleAmount = deductible,
            AccountingPeriod = date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(++_order),
        };
        _db.LedgerEntries.Add(entry);
        return entry;
    }

    private void Close(params string[] periods) => _db.PfaAccountingPeriods.AddRange(periods.Select(period =>
        new PfaAccountingPeriod { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Period = period, Status = AccountingPeriodStatus.Closed }));

    private int _assets;

    private PfaAsset Asset(string type, string description, DateOnly acquired, decimal value, DateOnly? disposed = null) => new()
    {
        Id = Guid.NewGuid(),
        PfaRegistrationId = _pfa,
        InventoryNumber = $"OI-{++_assets:0000}",
        Name = $"{type} — {description}",
        Kind = AssetKind.InventoryObject,
        DocumentRef = "Factura 1",
        EntryDate = acquired,
        InServiceDate = acquired,
        EntryValue = value,
        Status = disposed is null ? AssetStatus.Active : AssetStatus.Disposed,
        DisposalDate = disposed,
    };

    private async Task<RjipView> Rjip(DateOnly from, DateOnly to) =>
        (await new GetRjipQueryHandler(_db).Handle(new GetRjipQuery(_pfa, from, to), CancellationToken.None)).Value;

    private async Task<RefView> Ref(int year) =>
        (await new GetRefQueryHandler(_db).Handle(new GetRefQuery(_pfa, year), CancellationToken.None)).Value;

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
