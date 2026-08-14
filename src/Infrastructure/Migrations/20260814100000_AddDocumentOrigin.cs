using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// RL-07 — originea documentului, ca șoferul să vadă doar ce mai are de făcut.
    ///
    /// Până acum singurul mod de a distinge un dosar generat de noi de un act încărcat de client
    /// era categoria lui, ceea ce însemna că orice ecran își scria propria listă de excepții.
    /// Coloana mută regula într-un singur loc; backendul păstrează totul, iar generatorul de dosar
    /// ignoră complet flagul.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddDocumentOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin",
                schema: "public",
                table: "documents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "UserUpload");

            // Dosarele pe care le generăm noi: până acum se recunoșteau doar după categorie.
            migrationBuilder.Sql("""
                UPDATE public.documents
                SET origin = 'SystemGenerated'
                WHERE category IN (
                    'DosarAutorizatieArr',
                    'DosarCopieConformaEcusoane',
                    'SpecimenSemnatura');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "origin", schema: "public", table: "documents");
        }
    }
}
