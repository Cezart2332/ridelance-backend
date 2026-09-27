using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // InsertData cere un tablou bidimensional.

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Contabilitate PFA, etapa B7: categoria comisionului reținut de platformă, cheltuială deductibilă
    /// integral. O folosește ledger-ul când venitul se recunoaște brut din raport (GrossReport).
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddPlatformCommissionCategory : Migration
    {
        private static readonly Guid Id = new("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca709");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "public",
                table: "expense_category_rules",
                // Tipurile explicite: migrațiile nu au model în Designer, deci EF nu le poate deduce.
                columnTypes: ["uuid", "character varying(64)", "character varying(128)", "boolean", "character varying(48)", "character varying(256)", "date", "date"],
                columns: ["id", "category", "label", "vehicle_related", "default_deductibility", "counterparty_pattern", "valid_from", "valid_to"],
                values: new object[,]
                {
                    { Id, "PLATFORM_COMMISSION", "Comision platformă (Uber, Bolt)", false, "Percent100", null, new DateOnly(2025, 1, 1), null },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(schema: "public", table: "expense_category_rules", keyColumn: "id", keyColumnType: "uuid", keyValue: Id);
        }
    }
}
