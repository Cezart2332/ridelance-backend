using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// D100 pe data plății (spec declarații F20–F25): plățile către nerezidenți și decizia de impozit a
/// fiecăreia. Regulile pe furnizor rămân în registrul de furnizori (o singură sursă), deci copiile
/// lor din <c>tax_rules</c> se șterg.
/// </summary>
public partial class AddNonResidentPayments : Migration
{
    private const string Schema = "public";

    private static readonly string[] PaymentLookup = ["pfa_registration_id", "payment_date"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM public.tax_rules WHERE rule_type = 'NonResidentRate' AND supplier_entity_key IS NOT NULL;");

        migrationBuilder.CreateTable(
            name: "non_resident_payments",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                bank_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                commission_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                supplier_legal_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                supplier_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                supplier_tax_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                gross_income_ron = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                income_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_non_resident_payments", x => x.id);
                table.ForeignKey("fk_non_resident_payments_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, "pfa_registrations", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_non_resident_payments_bank_transactions_bank_transaction_id", x => x.bank_transaction_id, "bank_transactions", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_non_resident_payments_ledger_entries_ledger_entry_id", x => x.ledger_entry_id, "ledger_entries", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_non_resident_payments_platform_documents_commission_invoice_id", x => x.commission_invoice_id, "platform_documents", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_non_resident_payments_pfa_registration_id_payment_date", schema: Schema, table: "non_resident_payments", columns: PaymentLookup);
        migrationBuilder.CreateIndex(name: "ix_non_resident_payments_ledger_entry_id", schema: Schema, table: "non_resident_payments", column: "ledger_entry_id", unique: true, filter: "ledger_entry_id IS NOT NULL");
        migrationBuilder.CreateIndex(name: "ix_non_resident_payments_bank_transaction_id", schema: Schema, table: "non_resident_payments", column: "bank_transaction_id");
        migrationBuilder.CreateIndex(name: "ix_non_resident_payments_commission_invoice_id", schema: Schema, table: "non_resident_payments", column: "commission_invoice_id");

        migrationBuilder.CreateTable(
            name: "non_resident_tax_decisions",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                residence_certificate_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                treaty_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                applied_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                tax_rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                tax_due = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                obligation_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                confirmation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_non_resident_tax_decisions", x => x.id);
                table.ForeignKey("fk_non_resident_tax_decisions_non_resident_payments_payment_id", x => x.payment_id, "non_resident_payments", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_non_resident_tax_decisions_users_confirmed_by_user_id", x => x.confirmed_by_user_id, "users", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_non_resident_tax_decisions_payment_id", schema: Schema, table: "non_resident_tax_decisions", column: "payment_id", unique: true);
        migrationBuilder.CreateIndex(name: "ix_non_resident_tax_decisions_confirmed_by_user_id", schema: Schema, table: "non_resident_tax_decisions", column: "confirmed_by_user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "non_resident_tax_decisions", schema: Schema);
        migrationBuilder.DropTable(name: "non_resident_payments", schema: Schema);
    }
}
