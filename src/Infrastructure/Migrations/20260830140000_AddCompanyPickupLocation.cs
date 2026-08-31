using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Locul de unde se preiau mașinile: adresă, pin pe hartă și o indicație practică.
    ///
    /// Coloane, nu jsonb ca restul personalizării paginii: sediul social e text, dar ăsta are
    /// coordonate, iar coordonatele sunt exact genul de dată după care se caută la un moment dat
    /// („flote lângă mine"). Mașinile își țin deja pinul în coloane, din același motiv.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCompanyPickupLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pickup_address",
                schema: "public",
                table: "company_profiles",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "pickup_latitude",
                schema: "public",
                table: "company_profiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "pickup_longitude",
                schema: "public",
                table: "company_profiles",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_note",
                schema: "public",
                table: "company_profiles",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "pickup_address", schema: "public", table: "company_profiles");
            migrationBuilder.DropColumn(name: "pickup_latitude", schema: "public", table: "company_profiles");
            migrationBuilder.DropColumn(name: "pickup_longitude", schema: "public", table: "company_profiles");
            migrationBuilder.DropColumn(name: "pickup_note", schema: "public", table: "company_profiles");
        }
    }
}
