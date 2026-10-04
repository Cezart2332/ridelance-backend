using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1814 // InsertData cere un tablou bidimensional.

namespace Infrastructure.Migrations;

/// <summary>
/// Clasificarea tranzacțiilor bancare: propunerea din contrapartidă pe înregistrare (neaplicată până
/// la confirmare), regulile învățate pe contrapartidă („Aplică la toate similare”) și categoria
/// comisionului bancar.
/// </summary>
public partial class AddBankClassification : Migration
{
    private const string Schema = "public";
    private static readonly Guid BankFees = new("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca70a");
    private static readonly string[] RuleLookup = ["pfa_registration_id", "incoming", "name_key"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "proposed_classification", schema: Schema, table: "ledger_entries", type: "character varying(48)", maxLength: 48, nullable: true);

        migrationBuilder.CreateTable(
            name: "counterparty_classification_rules",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                incoming = table.Column<bool>(type: "boolean", nullable: false),
                name_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                classification = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_counterparty_classification_rules", x => x.id);
                table.ForeignKey("fk_counterparty_classification_rules_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, "pfa_registrations", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_counterparty_classification_rules_users_created_by_user_id", x => x.created_by_user_id, "users", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_counterparty_classification_rules_pfa_registration_id_incoming_name_key", schema: Schema, table: "counterparty_classification_rules", columns: RuleLookup);
        migrationBuilder.CreateIndex(name: "ix_counterparty_classification_rules_created_by_user_id", schema: Schema, table: "counterparty_classification_rules", column: "created_by_user_id");

        migrationBuilder.InsertData(
            schema: Schema,
            table: "expense_category_rules",
            columnTypes: ["uuid", "character varying(64)", "character varying(128)", "boolean", "character varying(48)", "character varying(256)", "date", "date"],
            columns: ["id", "category", "label", "vehicle_related", "default_deductibility", "counterparty_pattern", "valid_from", "valid_to"],
            values: new object[,]
            {
                { BankFees, "BANK_FEES", "Comision administrare cont bancar", false, "Percent100", null, new DateOnly(2025, 1, 1), null },
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(schema: Schema, table: "expense_category_rules", keyColumn: "id", keyColumnType: "uuid", keyValue: BankFees);
        migrationBuilder.DropTable(name: "counterparty_classification_rules", schema: Schema);
        migrationBuilder.DropColumn(name: "proposed_classification", schema: Schema, table: "ledger_entries");
    }
}
