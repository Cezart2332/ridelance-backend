using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// <c>driver_full_name</c> pe conturile de platformă.
    ///
    /// Pasul 5 cerea „ID șofer" — un identificator pe care șoferii nu-l știu pe de rost, deci
    /// câmpul rămânea gol aproape mereu. Îl înlocuiește numele de pe contul de șofer, care e și
    /// ce caută operatorul când leagă contul. <c>driver_external_id</c> rămâne în schemă pentru
    /// dosarele care l-au completat deja.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddPlatformDriverFullName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "driver_full_name",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "driver_full_name", schema: "public", table: "pfa_platform_accounts");
        }
    }
}
