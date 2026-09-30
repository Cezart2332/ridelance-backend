using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Spec flux contabil R30–R36. Bonul păstrează numărul, CUI-ul cumpărătorului și liniile cu sumă (partea
/// personală), iar potrivirile bon ↔ plată bancară apărute după înregistrarea manuală a bonului așteaptă
/// confirmarea într-un tabel propriu, fără să creeze o a doua înregistrare.
/// </summary>
public partial class AddReceiptLinesAndMatchProposals : Migration
{
    private static readonly string[] PfaTransactionColumns = ["pfa_registration_id", "bank_transaction_id"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "number", schema: "public", table: "expense_documents", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "beneficiary_cui", schema: "public", table: "expense_documents", type: "character varying(16)", maxLength: 16, nullable: true);
        migrationBuilder.AddColumn<string>(name: "lines_json", schema: "public", table: "expense_documents", type: "jsonb", nullable: false, defaultValue: "[]");

        migrationBuilder.CreateTable(
            name: "ledger_match_proposals",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                bank_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                accepted = table.Column<bool>(type: "boolean", nullable: true),
                resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ledger_match_proposals", x => x.id);
                table.ForeignKey(
                    name: "fk_ledger_match_proposals_pfa_registrations_pfa_registration_id",
                    column: x => x.pfa_registration_id,
                    principalSchema: "public",
                    principalTable: "pfa_registrations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_ledger_match_proposals_bank_transactions_bank_transaction_id",
                    column: x => x.bank_transaction_id,
                    principalSchema: "public",
                    principalTable: "bank_transactions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_ledger_match_proposals_ledger_entries_ledger_entry_id",
                    column: x => x.ledger_entry_id,
                    principalSchema: "public",
                    principalTable: "ledger_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_ledger_match_proposals_users_resolved_by_user_id",
                    column: x => x.resolved_by_user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_ledger_match_proposals_pfa_registration_id_bank_transaction_id",
            schema: "public",
            table: "ledger_match_proposals",
            columns: PfaTransactionColumns);

        migrationBuilder.CreateIndex(name: "ix_ledger_match_proposals_bank_transaction_id", schema: "public", table: "ledger_match_proposals", column: "bank_transaction_id");
        migrationBuilder.CreateIndex(name: "ix_ledger_match_proposals_ledger_entry_id", schema: "public", table: "ledger_match_proposals", column: "ledger_entry_id");
        migrationBuilder.CreateIndex(name: "ix_ledger_match_proposals_resolved_by_user_id", schema: "public", table: "ledger_match_proposals", column: "resolved_by_user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ledger_match_proposals", schema: "public");
        migrationBuilder.DropColumn(name: "number", schema: "public", table: "expense_documents");
        migrationBuilder.DropColumn(name: "beneficiary_cui", schema: "public", table: "expense_documents");
        migrationBuilder.DropColumn(name: "lines_json", schema: "public", table: "expense_documents");
    }
}
