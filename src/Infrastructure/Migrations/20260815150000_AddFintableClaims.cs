using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Trecerea pe Fintable. Providerul nu ne întoarce nimic după conectare și nu acceptă o
    /// referință de-a noastră pe link, deci proprietarul unei conexiuni devine o deducție:
    /// „ce conexiune a apărut, care nu exista înainte și nu e a nimănui".
    ///
    /// De aici cele trei adăugiri: momentul în care expiră linkul, lista conexiunilor existente
    /// la mintare, și jurnalul atribuirilor. Indexul unic pe `provider_connection_id` e plasa de
    /// siguranță — aceeași conexiune nu poate ajunge la doi utilizatori nici dacă logica greșește.
    ///
    /// Conexiunile existente rămân neatinse: aveau alt provider, iar `provider` de pe rând spune
    /// care. Nu se convertesc, pentru că nu există echivalent — utilizatorii lor vor reconecta.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddFintableClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "link_expires_at_utc",
                schema: "public",
                table: "bank_connections",
                type: "timestamp with time zone",
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
                    provider_connection_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    candidate_count = table.Column<int>(type: "integer", nullable: false),
                    claimed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_connection_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_connection_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bank_connection_claims_bank_connections_bank_connection_id",
                        column: x => x.bank_connection_id,
                        principalSchema: "public",
                        principalTable: "bank_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "ix_bank_connection_claims_bank_connection_id",
                schema: "public",
                table: "bank_connection_claims",
                column: "bank_connection_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "bank_connection_claims", schema: "public");
            migrationBuilder.DropColumn(name: "known_connection_ids_json", schema: "public", table: "bank_connections");
            migrationBuilder.DropColumn(name: "link_expires_at_utc", schema: "public", table: "bank_connections");
        }
    }
}
