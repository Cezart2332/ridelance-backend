using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Tranzacțiile fără un acord identificabil rămân în bază, dar nu mai sunt afișate.
/// Atribuirea lor acordului actual ar putea expune tranzacțiile unei alte persoane după
/// reconectarea aceluiași utilizator RIDElance la alt cont bancar.
/// </summary>
public partial class ScopeBankTransactionsByConsent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "provider_consent_id",
            schema: "public",
            table: "bank_transactions",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.DropIndex(
            name: "ix_bank_transactions_bank_account_id_provider_transaction_id",
            schema: "public",
            table: "bank_transactions");

        migrationBuilder.CreateIndex(
            name: "ix_bank_transactions_account_consent_transaction",
            schema: "public",
            table: "bank_transactions",
            columns: ["bank_account_id", "provider_consent_id", "provider_transaction_id"],
            unique: true);

        // Rândurile vechi sunt ascunse până la reimport; conturile active trebuie sincronizate
        // imediat după deploy, fără să aștepte cele 12 ore de la ultima rulare.
        migrationBuilder.Sql("UPDATE public.bank_accounts SET last_transactions_synced_at_utc = NULL WHERE is_active = TRUE;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_bank_transactions_account_consent_transaction",
            schema: "public",
            table: "bank_transactions");

        migrationBuilder.DropColumn(
            name: "provider_consent_id",
            schema: "public",
            table: "bank_transactions");

        migrationBuilder.CreateIndex(
            name: "ix_bank_transactions_bank_account_id_provider_transaction_id",
            schema: "public",
            table: "bank_transactions",
            columns: ["bank_account_id", "provider_transaction_id"],
            unique: true);
    }
}
