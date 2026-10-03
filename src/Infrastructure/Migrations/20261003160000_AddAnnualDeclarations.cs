using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1814 // InsertData cere un tablou bidimensional.

namespace Infrastructure.Migrations;

/// <summary>
/// Declarațiile anuale (spec declarații F40–F61): contractele de chirie de la persoane fizice și
/// plățile lor (ramura D205), răspunsurile anuale pentru D212, starea C801 pe casa de marcat și
/// regula cu tipul de activitate C801.
/// </summary>
public partial class AddAnnualDeclarations : Migration
{
    private const string Schema = "public";
    private const string Cash = "cash_register_states";

    private static readonly string[] AnswersKey = ["pfa_registration_id", "tax_year"];
    private static readonly string[] RentLookup = ["rental_contract_id", "payment_date"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            schema: Schema,
            table: "tax_rules",
            columnTypes: ["uuid", "character varying(48)", "character varying(2)", "date", "date", "character varying(500)", "numeric(9,4)", "numeric(18,2)",
                          "character varying(200)", "character varying(16)", "character varying(64)", "character varying(64)", "character varying(32)",
                          "character varying(32)", "boolean"],
            columns: AddTaxRules.Columns,
            values: new object[,]
            {
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070029"), "FormCode", "RO", new DateOnly(2016, 1, 1), null, "Instrucțiunile C801 (documentul sursă): tipul de activitate transport alternativ", null, null, "4", "C801", null, null, null, "RIDESHARING", true },
            });

        migrationBuilder.AddColumn<string>(name: "c801_status", schema: Schema, table: Cash, type: "character varying(48)", maxLength: 48, nullable: false, defaultValue: "NotStarted");
        migrationBuilder.AddColumn<Guid>(name: "c801_document_id", schema: Schema, table: Cash, type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "nui_number", schema: Schema, table: Cash, type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "vehicle_plate", schema: Schema, table: Cash, type: "character varying(16)", maxLength: 16, nullable: true);
        migrationBuilder.CreateIndex(name: "ix_cash_register_states_c801_document_id", schema: Schema, table: Cash, column: "c801_document_id");
        migrationBuilder.AddForeignKey(
            name: "fk_cash_register_states_documents_c801_document_id", schema: Schema, table: Cash, column: "c801_document_id",
            principalSchema: Schema, principalTable: "documents", principalColumn: "id", onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateTable(
            name: "rental_contracts",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                owner_cnp_encrypted = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                contract_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                contract_date = table.Column<DateOnly>(type: "date", nullable: false),
                gross_rent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                payment_frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                withholding_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                contract_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_rental_contracts", x => x.id);
                table.ForeignKey("fk_rental_contracts_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, "pfa_registrations", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_rental_contracts_tax_rules_withholding_rule_id", x => x.withholding_rule_id, "tax_rules", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_rental_contracts_documents_contract_document_id", x => x.contract_document_id, "documents", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_rental_contracts_pfa_registration_id", schema: Schema, table: "rental_contracts", column: "pfa_registration_id");
        migrationBuilder.CreateIndex(name: "ix_rental_contracts_withholding_rule_id", schema: Schema, table: "rental_contracts", column: "withholding_rule_id");
        migrationBuilder.CreateIndex(name: "ix_rental_contracts_contract_document_id", schema: Schema, table: "rental_contracts", column: "contract_document_id");

        migrationBuilder.CreateTable(
            name: "rent_payments",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                rental_contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                gross_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_rent_payments", x => x.id);
                table.ForeignKey("fk_rent_payments_rental_contracts_rental_contract_id", x => x.rental_contract_id, "rental_contracts", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_rent_payments_ledger_entries_ledger_entry_id", x => x.ledger_entry_id, "ledger_entries", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_rent_payments_rental_contract_id_payment_date", schema: Schema, table: "rent_payments", columns: RentLookup);
        migrationBuilder.CreateIndex(name: "ix_rent_payments_ledger_entry_id", schema: Schema, table: "rent_payments", column: "ledger_entry_id");

        migrationBuilder.CreateTable(
            name: "annual_tax_answers",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                tax_year = table.Column<int>(type: "integer", nullable: false),
                has_external_income = table.Column<bool>(type: "boolean", nullable: true),
                supplement_completed = table.Column<bool>(type: "boolean", nullable: false),
                anaf_prefilled_net_income = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                answered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                answered_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_annual_tax_answers", x => x.id);
                table.ForeignKey("fk_annual_tax_answers_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, "pfa_registrations", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_annual_tax_answers_users_answered_by_user_id", x => x.answered_by_user_id, "users", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_annual_tax_answers_pfa_registration_id_tax_year", schema: Schema, table: "annual_tax_answers", columns: AnswersKey, unique: true);
        migrationBuilder.CreateIndex(name: "ix_annual_tax_answers_answered_by_user_id", schema: Schema, table: "annual_tax_answers", column: "answered_by_user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "annual_tax_answers", schema: Schema);
        migrationBuilder.DropTable(name: "rent_payments", schema: Schema);
        migrationBuilder.DropTable(name: "rental_contracts", schema: Schema);
        migrationBuilder.DropForeignKey(name: "fk_cash_register_states_documents_c801_document_id", schema: Schema, table: Cash);
        migrationBuilder.DropIndex(name: "ix_cash_register_states_c801_document_id", schema: Schema, table: Cash);
        migrationBuilder.DropColumn(name: "c801_status", schema: Schema, table: Cash);
        migrationBuilder.DropColumn(name: "c801_document_id", schema: Schema, table: Cash);
        migrationBuilder.DropColumn(name: "nui_number", schema: Schema, table: Cash);
        migrationBuilder.DropColumn(name: "vehicle_plate", schema: Schema, table: Cash);
        migrationBuilder.DeleteData(schema: Schema, table: "tax_rules", keyColumn: "id", keyValue: new Guid("b1c2d3e4-f5a6-4b7c-8d9e-000000070029"));
    }
}
