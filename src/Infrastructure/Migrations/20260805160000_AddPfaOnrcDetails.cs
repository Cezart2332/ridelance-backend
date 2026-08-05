using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Datele pe care OCR-ul le citește din actele ONRC ale unui PFA deja înființat: titularul
    /// și sediul profesional din certificatul de înregistrare, activitățile autorizate, locul
    /// desfășurării și punctele de lucru din certificatul constatator.
    ///
    /// Sediul profesional și activitățile se păstrează ca text, exact cum apar în acte: sunt
    /// pentru dosarul ARR și pentru verificarea adminului, nu pentru procesare structurată.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddPfaOnrcDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "holder_name",
                schema: "public",
                table: "pfa_registrations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "professional_office",
                schema: "public",
                table: "pfa_registrations",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "authorized_activities",
                schema: "public",
                table: "pfa_registrations",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "activity_location",
                schema: "public",
                table: "pfa_registrations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "work_points",
                schema: "public",
                table: "pfa_registrations",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "work_points", schema: "public", table: "pfa_registrations");
            migrationBuilder.DropColumn(name: "activity_location", schema: "public", table: "pfa_registrations");
            migrationBuilder.DropColumn(name: "authorized_activities", schema: "public", table: "pfa_registrations");
            migrationBuilder.DropColumn(name: "professional_office", schema: "public", table: "pfa_registrations");
            migrationBuilder.DropColumn(name: "holder_name", schema: "public", table: "pfa_registrations");
        }
    }
}
