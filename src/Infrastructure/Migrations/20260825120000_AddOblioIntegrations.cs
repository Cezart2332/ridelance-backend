using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// <c>oblio_integrations</c> — contul Oblio al fiecărui proprietar.
    ///
    /// Platforma are deja un cont Oblio, în configurare, pe care emite facturile ei. Tabela asta
    /// e pentru direcția inversă: un PFA sau un SRL își facturează propriii clienți, pe CIF-ul
    /// lui, deci are nevoie de credențialele lui.
    ///
    /// Secretul se stochează criptat cu <c>ISecretProtector</c>, ca IBAN-ul și CNP-ul, și nu se
    /// întoarce niciodată către frontend.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddOblioIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "oblio_integrations",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    client_secret_encrypted = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    cif = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    series_name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    company_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_connected = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    error_message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    last_sync_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oblio_integrations", x => x.id);
                    // Ștergerea contului duce integrarea cu el: credențiale fără proprietar nu au sens.
                    table.ForeignKey(
                        name: "fk_oblio_integrations_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_oblio_integrations_user_id",
                schema: "public",
                table: "oblio_integrations",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "oblio_integrations", schema: "public");
        }
    }
}
