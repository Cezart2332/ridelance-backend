using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Textul PDF-ului documentelor Uber/Bolt (spec contabilitate B1), extras o dată la citire:
    /// verificarea „sumele apar în text” se reface fără să decripteze fișierul la fiecare citire.
    /// `has_text_layer` spune dacă documentul a fost citit din text sau ca imagine.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddPlatformDocumentText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pdf_text",
                schema: "public",
                table: "platform_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_text_layer",
                schema: "public",
                table: "platform_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "pdf_text", schema: "public", table: "platform_documents");
            migrationBuilder.DropColumn(name: "has_text_layer", schema: "public", table: "platform_documents");
        }
    }
}
