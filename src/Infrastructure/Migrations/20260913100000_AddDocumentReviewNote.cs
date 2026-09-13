using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// <c>review_note</c> pe documente: motivul respingerii scris de admin sau contabil.
    ///
    /// Un document respins de un om ajungea la șofer doar ca „Respins" — cerc roșu, fără nicio
    /// explicație. Motivul automat (<c>ai_summary</c>) exista doar pentru respingerile AI.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddDocumentReviewNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "review_note",
                schema: "public",
                table: "documents",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "review_note", schema: "public", table: "documents");
        }
    }
}
