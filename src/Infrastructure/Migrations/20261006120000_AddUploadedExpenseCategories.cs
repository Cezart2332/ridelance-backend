using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1814

namespace Infrastructure.Migrations;

public partial class AddUploadedExpenseCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.InsertData(
        schema: "public", table: "expense_category_rules",
        columnTypes: ["uuid", "character varying(64)", "character varying(128)", "boolean", "character varying(48)", "character varying(256)", "date", "date"],
        columns: ["id", "category", "label", "vehicle_related", "default_deductibility", "counterparty_pattern", "valid_from", "valid_to"],
        values: new object[,]
        {
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca711"), "ACCOUNTING", "Servicii de contabilitate", false, "Percent100", "CONTABIL", new DateOnly(2025, 1, 1), null },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca712"), "SOFTWARE", "Software și abonamente pentru activitate", false, "Percent100", null, new DateOnly(2025, 1, 1), null },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca713"), "CAR_RENTAL", "Chirie auto", true, "Percent100", null, new DateOnly(2025, 1, 1), null },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca714"), "EV_CHARGING", "Încărcare electrică auto", true, "Percent100", "ELDRIVE|RENOVATIO", new DateOnly(2025, 1, 1), null },
            { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca715"), "OTHER_BUSINESS", "Alte cheltuieli — tratament de verificat", false, "SpecialRule", null, new DateOnly(2025, 1, 1), null },
        });

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        for (int index = 711; index <= 715; index++)
        {
            migrationBuilder.DeleteData(schema: "public", table: "expense_category_rules", keyColumn: "id", keyColumnType: "uuid",
                keyValue: new Guid($"b1c2d3e4-f5a6-4b7c-8d9e-0000000ca{index}"));
        }
    }
}
