using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Trecerea open bankingului pe Smart Accounts.
    ///
    /// Furnizorul anterior nu întorcea utilizatorul înapoi la noi și ținea toate conexiunile
    /// clienților la un loc, așa că proprietatea unei conexiuni se deducea: notam ce exista înainte
    /// (<c>known_connection_ids_json</c>) și revendicam ce apărea nou (<c>bank_connection_claims</c>).
    /// La un furnizor PSD2 consimțământul e al nostru din prima secundă, deci nu mai e nimic de
    /// ghicit — ambele dispar.
    ///
    /// În locul lor intră ce cere un consimțământ PSD2: identificatorul lui, în clar fiindcă după
    /// el se caută rândul, și perechea de tokenuri, criptată.
    ///
    /// Conexiunile rămase de la furnizorul vechi se marchează revocate: identificatorii lor nu au
    /// echivalent la noul furnizor, iar o conexiune „legată" pe care nu se mai poate citi nimic e
    /// mai rea decât una care cere reconectarea. Utilizatorii vor reconecta banca o dată.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class SwitchToSmartAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "bank_connection_claims", schema: "public");

            migrationBuilder.DropColumn(
                name: "known_connection_ids_json", schema: "public", table: "bank_connections");

            migrationBuilder.DropColumn(
                name: "provider_agreement_id", schema: "public", table: "bank_connections");

            migrationBuilder.RenameColumn(
                name: "provider_requisition_id",
                schema: "public",
                table: "bank_connections",
                newName: "provider_consent_id");

            migrationBuilder.AlterColumn<string>(
                name: "provider_consent_id",
                schema: "public",
                table: "bank_connections",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AddColumn<string>(
                name: "access_token_encrypted",
                schema: "public",
                table: "bank_connections",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refresh_token_encrypted",
                schema: "public",
                table: "bank_connections",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "access_token_expires_at_utc",
                schema: "public",
                table: "bank_connections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "consent_status",
                schema: "public",
                table: "bank_connections",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // Rândurile vechi: identificatorul lor era criptat cu cheia noastră și numea o
            // conexiune la un furnizor de care nu mai depindem. Golit, ar sparge indexul unic de
            // mai jos dacă ar rămâne mai multe — de aceea primesc fiecare o valoare distinctă.
            migrationBuilder.Sql("""
                UPDATE public.bank_connections
                SET provider_consent_id = 'legacy-' || id::text,
                    status = 5,
                    error_message = NULL
                WHERE provider <> 'SmartAccounts';
                """);

            migrationBuilder.CreateIndex(
                name: "ix_bank_connections_provider_consent_id",
                schema: "public",
                table: "bank_connections",
                column: "provider_consent_id",
                unique: true);

            // Identificatorul de cont e al băncii, iar la unele bănci chiar IBAN-ul e
            // identificatorul: unic global, doi utilizatori cu conturi la aceeași bancă s-ar ciocni.
            migrationBuilder.DropIndex(
                name: "ix_bank_accounts_provider_account_id", schema: "public", table: "bank_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_bank_connection_id_provider_account_id",
                schema: "public",
                table: "bank_accounts",
                columns: ["bank_connection_id", "provider_account_id"],
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bank_accounts_bank_connection_id_provider_account_id",
                schema: "public",
                table: "bank_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_provider_account_id",
                schema: "public",
                table: "bank_accounts",
                column: "provider_account_id",
                unique: true);

            migrationBuilder.DropIndex(
                name: "ix_bank_connections_provider_consent_id",
                schema: "public",
                table: "bank_connections");

            migrationBuilder.DropColumn(
                name: "consent_status", schema: "public", table: "bank_connections");

            migrationBuilder.DropColumn(
                name: "access_token_expires_at_utc", schema: "public", table: "bank_connections");

            migrationBuilder.DropColumn(
                name: "refresh_token_encrypted", schema: "public", table: "bank_connections");

            migrationBuilder.DropColumn(
                name: "access_token_encrypted", schema: "public", table: "bank_connections");

            migrationBuilder.AlterColumn<string>(
                name: "provider_consent_id",
                schema: "public",
                table: "bank_connections",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.RenameColumn(
                name: "provider_consent_id",
                schema: "public",
                table: "bank_connections",
                newName: "provider_requisition_id");

            migrationBuilder.AddColumn<string>(
                name: "provider_agreement_id",
                schema: "public",
                table: "bank_connections",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "known_connection_ids_json",
                schema: "public",
                table: "bank_connections",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bank_connection_claims",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_connection_id = table.Column<string>(
                        type: "character varying(128)", maxLength: 128, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    candidate_count = table.Column<int>(type: "integer", nullable: false),
                    claimed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_connection_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_connection_claims_bank_connections_bank_connection_id",
                        column: x => x.bank_connection_id,
                        principalSchema: "public",
                        principalTable: "bank_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bank_connection_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_connection_claims_bank_connection_id",
                schema: "public",
                table: "bank_connection_claims",
                column: "bank_connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_connection_claims_provider_connection_id",
                schema: "public",
                table: "bank_connection_claims",
                column: "provider_connection_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_connection_claims_user_id",
                schema: "public",
                table: "bank_connection_claims",
                column: "user_id");
        }
    }
}
