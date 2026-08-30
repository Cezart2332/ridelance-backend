using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Mini-site-ul firmei devine personalizabil: slogan, fotografie de cover, culori proprii și
    /// conținutul secțiunilor (avantaje, program, zone de predare, întrebări frecvente).
    ///
    /// Culorile și conținutul stau în jsonb, nu în coloane: se citesc întotdeauna împreună cu
    /// profilul, nu se caută niciodată după ele, iar forma lor se va mai schimba pe măsură ce
    /// apar secțiuni noi. Valoarea implicită e obiectul gol, ca rândurile existente să nu aibă
    /// nevoie de o migrare de date — codul completează implicitele la deserializare.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCompanyPageCustomisation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tagline",
                schema: "public",
                table: "company_profiles",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cover_image_url",
                schema: "public",
                table: "company_profiles",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "page_theme",
                schema: "public",
                table: "company_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "page_content",
                schema: "public",
                table: "company_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tagline",
                schema: "public",
                table: "company_profiles");

            migrationBuilder.DropColumn(
                name: "cover_image_url",
                schema: "public",
                table: "company_profiles");

            migrationBuilder.DropColumn(
                name: "page_theme",
                schema: "public",
                table: "company_profiles");

            migrationBuilder.DropColumn(
                name: "page_content",
                schema: "public",
                table: "company_profiles");
        }
    }
}
