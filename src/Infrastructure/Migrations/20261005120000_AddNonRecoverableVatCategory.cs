using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1814 // EF InsertData uses a multidimensional array.

namespace Infrastructure.Migrations;

public partial class AddNonRecoverableVatCategory : Migration
{
    private static readonly Guid CategoryId = new("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca710");

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.InsertData(
        schema: "public", table: "expense_category_rules",
        columnTypes: ["uuid", "character varying(64)", "character varying(128)", "boolean", "character varying(48)", "character varying(256)", "date", "date"],
        columns: ["id", "category", "label", "vehicle_related", "default_deductibility", "counterparty_pattern", "valid_from", "valid_to"],
        values: new object[,] { { CategoryId, "NON_RECOVERABLE_VAT", "TVA nerecuperabil (D301)", false, "Percent100", null, new DateOnly(2025, 1, 1), null } });

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DeleteData(
        schema: "public", table: "expense_category_rules", keyColumn: "id", keyColumnType: "uuid", keyValue: CategoryId);
}
