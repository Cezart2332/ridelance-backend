using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Documentele generate își păstrează sursa.
    ///
    /// Fără ea, un document nu se poate retipări cu semnătura pe el decât recompunându-l din datele
    /// de azi ale închirierii — adică riscând să iasă alt document decât cel care a fost semnat.
    ///
    /// Coloanele rămân goale pentru documentele generate până acum. Acelea se pot semna în
    /// continuare, dar nu vor avea variantă tipărită cu semnătura; se obține regenerându-le.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddGeneratedDocumentSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_file_path",
                schema: "public",
                table: "generated_documents",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_iv",
                schema: "public",
                table: "generated_documents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_file_path",
                schema: "public",
                table: "generated_documents");

            migrationBuilder.DropColumn(
                name: "source_iv",
                schema: "public",
                table: "generated_documents");
        }
    }
}
