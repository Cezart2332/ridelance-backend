using System.Text.Json;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// B0: modelul de date al contabilității. Migrația e scrisă de mână (proiectul nu întreține
/// snapshot-ul), deci testul verifică direct că tabelele și coloanele ei sunt exact cele din model.
/// </summary>
public sealed class AccountingModelTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Migration_creates_exactly_the_accounting_tables_and_columns_of_the_model()
    {
        IModel model = NpgsqlModel();
        var expected = AccountingEntityTypes(model)
            .ToDictionary(
                type => type.GetTableName()!,
                type =>
                {
                    var table = StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema());
                    return type.GetProperties()
                        .Select(property => property.GetColumnName(table)!)
                        // `xmin` e coloana de sistem a Postgres: există în orice tabel, nu se creează.
                        .Where(column => column != "xmin")
                        .ToHashSet();
                });

        List<CreateTableOperation> created = [.. new AddAccountingModule().UpOperations.OfType<CreateTableOperation>()];

        created.Select(table => table.Name).ToHashSet().ShouldBe(expected.Keys.ToHashSet(), ignoreOrder: true);
        foreach (CreateTableOperation table in created)
        {
            table.Columns.Select(column => column.Name).ToHashSet().ShouldBe(expected[table.Name], ignoreOrder: true, customMessage: table.Name);
        }
    }

    [Fact]
    public void No_foreign_key_from_accounting_data_cascades()
    {
        IModel model = NpgsqlModel();

        var cascading = AccountingEntityTypes(model)
            .SelectMany(type => type.GetForeignKeys())
            .Where(foreignKey => foreignKey.DeleteBehavior != DeleteBehavior.Restrict)
            .Select(foreignKey => $"{foreignKey.DeclaringEntityType.ClrType.Name}.{string.Join(",", foreignKey.Properties.Select(p => p.Name))}")
            .ToList();

        cascading.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(typeof(DeclarationVersion))]
    [InlineData(typeof(LedgerEntry))]
    public void Concurrency_token_is_xmin(Type entity)
    {
        IProperty? xmin = NpgsqlModel().FindEntityType(entity)!.FindProperty("xmin");

        xmin.ShouldNotBeNull();
        xmin.IsConcurrencyToken.ShouldBeTrue();
    }

    [Fact]
    public void Seed_has_vat_suppliers_d100_rules_and_retention()
    {
        List<InsertDataOperation> inserts = [.. new AddAccountingModule().UpOperations.OfType<InsertDataOperation>()];
        InsertDataOperation Table(string name) => inserts.Single(insert => insert.Table == name);

        Rows(Table("vat_rates"), "rate").ShouldBe([19m, 21m]);

        InsertDataOperation suppliers = Table("supplier_tax_profiles");
        Rows(suppliers, "supplier_name").ShouldBe(["Bolt Operations OÜ", "Uber B.V."]);
        Rows(suppliers, "d100rate").ShouldBe([2m, null]);
        Rows(suppliers, "d100rate_confirmed").ShouldBe([true, false]);

        InsertDataOperation rules = Table("d100_rules");
        Rows(rules, "code").ShouldBe(["D100CommissionNonresident", "D100RentIndividual"]);
        Rows(rules, "enabled").ShouldBe([true, false]);

        InsertDataOperation retention = Table("retention_policies");
        Rows(retention, "years_after").ShouldBe([5]);
        Rows(retention, "start_month").ShouldBe([7]);
        Rows(retention, "start_day").ShouldBe([1]);
    }

    [Fact]
    public async Task Accounting_records_cannot_be_deleted()
    {
        await using ApplicationDbContext db = InMemoryDb();
        var rate = new VatRate { Id = Guid.NewGuid(), Rate = 21m, ValidFrom = new DateOnly(2025, 8, 1) };
        db.VatRates.Add(rate);
        await db.SaveChangesAsync();

        db.VatRates.Remove(rate);

        InvalidOperationException error = await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        error.Message.ShouldContain(nameof(VatRate));
    }

    [Theory]
    [InlineData(PlatformDocumentStatus.NeedsReview, "\"NEEDS_REVIEW\"")]
    [InlineData(DeclarationStatus.ReadyToSign, "\"READY_TO_SIGN\"")]
    [InlineData(LedgerSource.CashZ, "\"CASH_Z\"")]
    [InlineData(CashRegisterStatus.NotRequiredCurrentConfiguration, "\"NOT_REQUIRED_CURRENT_CONFIGURATION\"")]
    [InlineData(D100RuleCode.D100CommissionNonresident, "\"D100_COMMISSION_NONRESIDENT\"")]
    [InlineData(DeclarationType.D301, "\"D301\"")]
    [InlineData(DeductibilityType.Percent100, "\"100_PERCENT\"")]
    [InlineData(DeductibilityType.Percent50, "\"50_PERCENT\"")]
    [InlineData(DocumentCheckCode.NotAlreadyDeclared, "\"NOT_ALREADY_DECLARED\"")]
    public void Enums_serialize_like_the_typescript_contract(object value, string json)
    {
        JsonSerializer.Serialize(value, value.GetType(), Web).ShouldBe(json);
        JsonSerializer.Deserialize(json, value.GetType(), Web).ShouldBe(value);
    }

    [Fact]
    public void Dtos_use_the_contract_property_names()
    {
        var summary = new DeclarationSummary(
            Guid.Empty, Guid.Empty, "2026-08", DeclarationType.D100, DeclarationStatus.Generated, 20m, null, 1, DeclarationVersionKind.Initial, []);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, Web));
        JsonElement root = document.RootElement;

        root.GetProperty("declarationId").GetGuid().ShouldBe(Guid.Empty);
        root.GetProperty("period").GetString().ShouldBe("2026-08");
        root.GetProperty("status").GetString().ShouldBe("GENERATED");
        root.GetProperty("currentVersionKind").GetString().ShouldBe("INITIAL");
        root.GetProperty("blockingReasons").GetArrayLength().ShouldBe(0);
    }

    private static List<object?> Rows(InsertDataOperation insert, string column)
    {
        int index = Array.IndexOf(insert.Columns, column);
        index.ShouldBeGreaterThanOrEqualTo(0, $"{insert.Table}.{column}");
        return [.. Enumerable.Range(0, insert.Values.GetLength(0)).Select(row => insert.Values[row, index])];
    }

    private static IEnumerable<IEntityType> AccountingEntityTypes(IModel model) =>
        model.GetEntityTypes().Where(type => type.ClrType.Namespace == typeof(PlatformDocument).Namespace);

    /// <summary>Modelul relațional complet, pe Npgsql, fără conexiune la bază.</summary>
    private static IModel NpgsqlModel()
    {
        using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only")
                .UseSnakeCaseNamingConvention()
                .Options,
            new Events());
        return db.GetService<IDesignTimeModel>().Model;
    }

    private static ApplicationDbContext InMemoryDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
