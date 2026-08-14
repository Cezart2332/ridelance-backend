using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Ce cere formularul de lead din pagina de detaliu (spec §17): data dorită, durata, dacă
    /// vizitatorul are deja cont pe o platformă, un mesaj liber și momentul acordului GDPR.
    ///
    /// Pentru cererile deja existente, `consent_accepted_at_utc` primește data trimiterii. Nu e o
    /// invenție: acordul a fost dat atunci, doar că nu era stocat separat. `intent` devine
    /// „Request”, singurul lucru care se putea trimite până acum.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCarLeadDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "intent",
                schema: "public",
                table: "car_leads",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Request");

            migrationBuilder.AddColumn<DateOnly>(
                name: "preferred_start_date",
                schema: "public",
                table: "car_leads",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "weeks",
                schema: "public",
                table: "car_leads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_platform_account",
                schema: "public",
                table: "car_leads",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "message",
                schema: "public",
                table: "car_leads",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "consent_accepted_at_utc",
                schema: "public",
                table: "car_leads",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.Sql("""
                UPDATE public.car_leads
                SET consent_accepted_at_utc = created_at_utc;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "consent_accepted_at_utc", schema: "public", table: "car_leads");
            migrationBuilder.DropColumn(name: "message", schema: "public", table: "car_leads");
            migrationBuilder.DropColumn(name: "has_platform_account", schema: "public", table: "car_leads");
            migrationBuilder.DropColumn(name: "weeks", schema: "public", table: "car_leads");
            migrationBuilder.DropColumn(name: "preferred_start_date", schema: "public", table: "car_leads");
            migrationBuilder.DropColumn(name: "intent", schema: "public", table: "car_leads");
        }
    }
}
