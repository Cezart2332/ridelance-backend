using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// <c>ai_rotation_degrees</c> pe documente: cu cât trebuie rotită poza ca actul să fie drept.
    ///
    /// Valoarea o dă modelul care oricum citește fiecare document. Nu se poate deduce din pixeli:
    /// pe o diplomă reală, scorurile statistice ale celor patru rotații au ieșit −0.043 / −0.053 /
    /// −0.060 / −0.060, adică zgomot. Se folosește la generarea dosarelor, ca actul fotografiat
    /// culcat să nu ajungă culcat și la ghișeu.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddDocumentAiRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ai_rotation_degrees",
                schema: "public",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_rotation_degrees", schema: "public", table: "documents");
        }
    }
}
