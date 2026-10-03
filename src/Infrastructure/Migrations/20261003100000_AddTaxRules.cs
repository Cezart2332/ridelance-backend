using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1814 // InsertData cere un tablou bidimensional.

namespace Infrastructure.Migrations;

/// <summary>
/// Regulile fiscale versionate (spec declarații §4): coduri de obligație, coduri bugetare, termene,
/// statele UE, cursul, rotunjirea, materialitatea, cotele de nerezident și de chirie. Seed-ul e
/// <c>Application.Accounting.Tax.TaxRuleSeed</c>; un test verifică că sunt identice.
/// </summary>
public partial class AddTaxRules : Migration
{
    private const string Table = "tax_rules";

    private static readonly string[] Lookup = ["rule_type", "jurisdiction", "valid_from"];

    internal static readonly string[] Columns =
        ["id", "rule_type", "jurisdiction", "valid_from", "valid_to", "legal_basis", "rate", "threshold", "formula", "declaration_code",
         "anaf_form_version", "validator_version", "supplier_entity_key", "income_type", "confirmed"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: Table,
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                rule_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                jurisdiction = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                legal_basis = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                threshold = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                formula = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                declaration_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                anaf_form_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                validator_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                supplier_entity_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                income_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                confirmed = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_tax_rules", x => x.id));
        migrationBuilder.CreateIndex(name: "ix_tax_rules_rule_type_jurisdiction_valid_from", schema: "public", table: Table, columns: Lookup);

        migrationBuilder.InsertData(
            schema: "public",
            table: Table,
            columnTypes: ["uuid", "character varying(48)", "character varying(2)", "date", "date", "character varying(500)", "numeric(9,4)", "numeric(18,2)",
                          "character varying(200)", "character varying(16)", "character varying(64)", "character varying(64)", "character varying(32)",
                          "character varying(32)", "boolean"],
            columns: Columns,
            values: new object[,]
            {
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070001"), "ObligationCode", "RO", new DateOnly(2016, 1, 1), null, "Nomenclatorul obligațiilor fiscale (OPANAF privind D100)", null, null, "634", "D100", null, null, null, "COMMISSION", true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070002"), "ObligationCode", "RO", new DateOnly(2016, 1, 1), null, "Instrucțiunile de completare ale formularului (documentul sursă, 29.09.2026)", null, null, "301", "D301", null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070003"), "BudgetCode", "RO", new DateOnly(2016, 1, 1), null, "Nomenclatorul obligațiilor fiscale (OPANAF privind D100)", null, null, "5503XXXXXX", "D100", null, null, null, "COMMISSION", true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070004"), "Deadline", "RO", new DateOnly(2016, 1, 1), null, "Instrucțiunile de completare ale formularului (documentul sursă, 29.09.2026)", null, null, "MONTHLY:25", "D100", null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070005"), "Deadline", "RO", new DateOnly(2016, 1, 1), null, "Instrucțiunile de completare ale formularului (documentul sursă, 29.09.2026)", null, null, "MONTHLY:25", "D301", null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070006"), "Deadline", "RO", new DateOnly(2016, 1, 1), null, "Instrucțiunile de completare ale formularului (documentul sursă, 29.09.2026)", null, null, "MONTHLY:25", "D390", null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070007"), "Deadline", "RO", new DateOnly(2016, 1, 1), null, "Instrucțiunile de completare ale formularului (documentul sursă, 29.09.2026)", null, null, "ANNUAL:02-LAST", "D207", null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070008"), "Deadline", "RO", new DateOnly(2016, 1, 1), null, "Instrucțiunile de completare ale formularului (documentul sursă, 29.09.2026)", null, null, "ANNUAL:05-25", "D212", null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070009"), "Deadline", "RO", new DateOnly(2016, 1, 1), null, "Instrucțiunile de completare ale formularului (documentul sursă, 29.09.2026)", null, null, "ANNUAL:02-LAST", "D205", null, null, null, null, false },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-00000007000a"), "Rounding", "RO", new DateOnly(2016, 1, 1), null, "XSD-ul ANAF D100 nu acceptă bani pentru sume", null, null, "WholeLei", "D100", null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-00000007000b"), "ExchangeRate", "RO", new DateOnly(2016, 1, 1), null, "Cursul BNR (Q1, de confirmat)", null, null, "SameDayOrPrevious", null, null, null, null, null, false },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-00000007000c"), "Materiality", "RO", new DateOnly(2016, 1, 1), null, "Pragul de materialitate pentru payout-uri nereconciliate (Q2)", null, 1m, null, null, null, null, null, null, false },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-00000007000d"), "NonResidentRate", "RO", new DateOnly(2016, 1, 1), null, "Codul fiscal, Titlul VI (impozitul pe veniturile nerezidenților), cota standard", 16m, null, null, null, null, null, null, "COMMISSION", false },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-00000007000e"), "RentWithholding", "RO", new DateOnly(2016, 1, 1), null, "Codul fiscal, Titlul IV (venituri din cedarea folosinței bunurilor), de confirmat", 10m, null, "WITHHOLD_ON_PAYMENT", null, null, null, null, "RENT", false },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-00000007000f"), "EuMember", "AT", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070010"), "EuMember", "BE", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070011"), "EuMember", "BG", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070012"), "EuMember", "CY", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070013"), "EuMember", "CZ", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070014"), "EuMember", "DE", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070015"), "EuMember", "DK", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070016"), "EuMember", "EE", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070017"), "EuMember", "EL", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070018"), "EuMember", "ES", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070019"), "EuMember", "FI", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-00000007001a"), "EuMember", "FR", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-00000007001b"), "EuMember", "HR", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-00000007001c"), "EuMember", "HU", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-00000007001d"), "EuMember", "IE", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-00000007001e"), "EuMember", "IT", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-00000007001f"), "EuMember", "LT", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070020"), "EuMember", "LU", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070021"), "EuMember", "LV", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070022"), "EuMember", "MT", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070023"), "EuMember", "NL", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070024"), "EuMember", "PL", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070025"), "EuMember", "PT", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070026"), "EuMember", "SE", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070027"), "EuMember", "SI", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070028"), "EuMember", "SK", new DateOnly(2016, 1, 1), null, "Tratatul de aderare / statele membre UE", null, null, null, null, null, null, null, null, true },
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: Table, schema: "public");
}
