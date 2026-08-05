using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Readuce parola contului de flotă pe <c>pfa_platform_accounts</c>, ștearsă în
    /// <c>20260722190000_DropPlatformPasswordColumns</c>.
    ///
    /// Se stochează criptată cu ISecretProtector, ca IBAN-ul și CNP-ul, și nu se întoarce
    /// niciodată către client — API-ul raportează doar dacă există.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddPlatformFleetCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "password_protected",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "password_updated_at_utc",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "password_updated_at_utc",
                schema: "public",
                table: "pfa_platform_accounts");

            migrationBuilder.DropColumn(
                name: "password_protected",
                schema: "public",
                table: "pfa_platform_accounts");
        }
    }
}
