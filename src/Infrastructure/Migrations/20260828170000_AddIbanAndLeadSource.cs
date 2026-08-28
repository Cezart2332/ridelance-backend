using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Contul bancar al firmei și sursa de trafic a unei cereri.
    ///
    /// IBAN-ul apare tipărit pe contracte și facturi, deci se ține în clar — spre deosebire de
    /// IBAN-urile personale din onboardingul PFA, care se criptează.
    ///
    /// Sursa cererilor pornește de la „vdp" pentru tot ce există: rândurile vechi chiar au venit
    /// direct de pe pagina anunțului, fiindcă până acum n-aveam de unde altundeva să le luăm.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddIbanAndLeadSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "iban",
                schema: "public",
                table: "company_profiles",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "public",
                table: "car_leads",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "vdp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "source", schema: "public", table: "car_leads");
            migrationBuilder.DropColumn(name: "iban", schema: "public", table: "company_profiles");
        }
    }
}
