using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Contabilitate PFA, documente reale Uber/Bolt: „Data impozitării” de pe factură (luna fiscală a
    /// facturilor săptămânale Uber) și reținerea la sursă raportată de platformă (rezumatul Bolt).
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddTaxPointAndWithheldTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "tax_point_date",
                schema: "public",
                table: "document_extractions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "withheld_tax",
                schema: "public",
                table: "document_extractions",
                type: "numeric(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "tax_point_date", schema: "public", table: "document_extractions");
            migrationBuilder.DropColumn(name: "withheld_tax", schema: "public", table: "document_extractions");
        }
    }
}
