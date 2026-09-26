using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Contabilitate PFA, etapa B5: regenerarea unei versiuni de declarație nu șterge liniile vechi,
    /// le marchează înlocuite (`superseded_at_utc`). Nimic nu se șterge fizic (spec §0 pct. 7).
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddDeclarationLineSupersede : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "superseded_at_utc",
                schema: "public",
                table: "declaration_lines",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "superseded_at_utc", schema: "public", table: "declaration_lines");
        }
    }
}
