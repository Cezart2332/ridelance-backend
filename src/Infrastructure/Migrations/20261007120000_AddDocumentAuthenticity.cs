using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Verificarea de autenticitate a documentelor de înrolare.
    ///
    /// <c>ai_suspicion_reasons</c>: de ce arată un document suspect (fără ștampilă, PDF din Word,
    /// titular diferit de buletin). <c>ai_identity_mismatch</c>: documentul pare al altei persoane,
    /// deci datele citite din el nu se aplică pe profil până nu decide adminul.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddDocumentAuthenticity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ai_suspicion_reasons",
                schema: "public",
                table: "documents",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ai_identity_mismatch",
                schema: "public",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ai_suspicion_reasons", schema: "public", table: "documents");
            migrationBuilder.DropColumn(name: "ai_identity_mismatch", schema: "public", table: "documents");
        }
    }
}
