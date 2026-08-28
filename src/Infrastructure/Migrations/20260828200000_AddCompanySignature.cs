using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Firma își poate păstra un specimen de semnătură.
    ///
    /// Se tipărește pe contractele și procesele-verbale generate, ca proprietarul să nu semneze de
    /// mână fiecare document. Documentul generat reține specimenul folosit la tipărirea lui, ca o
    /// schimbare de semnătură să nu rescrie retroactiv ce s-a trimis deja la semnat.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCompanySignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "signature_document_id",
                schema: "public",
                table: "company_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "company_signature_document_id",
                schema: "public",
                table: "generated_documents",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "signature_document_id",
                schema: "public",
                table: "company_profiles");

            migrationBuilder.DropColumn(
                name: "company_signature_document_id",
                schema: "public",
                table: "generated_documents");
        }
    }
}
