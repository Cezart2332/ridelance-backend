using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // InsertData cere un tablou bidimensional.

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Contabilitate PFA, etapa B4: schemele ANAF D100 / D301 / D390 (XSD-urile oficiale din
    /// <c>Accounting/Anaf/Schemas</c>) și versiunea kitului DUKIntegrator din serviciul de validare.
    /// </summary>
    public partial class AddAnafSchemas : Migration
    {
        private static readonly Guid D100 = new("7a3e0f52-5b8c-4f0e-9d2a-000000000100");
        private static readonly Guid D301 = new("7a3e0f52-5b8c-4f0e-9d2a-000000000301");
        private static readonly Guid D390 = new("7a3e0f52-5b8c-4f0e-9d2a-000000000390");

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "public",
                table: "anaf_declaration_schemas",
                // Tipurile explicite: migrațiile nu au model în Designer, deci EF nu le poate deduce.
                columnTypes: ["uuid", "character varying(48)", "character varying(64)", "character varying(256)", "character varying(64)", "date", "date"],
                columns: ["id", "declaration_type", "version", "xsd_path", "validator_version", "valid_from", "valid_to"],
                values: new object[,]
                {
                    { D100, "D100", "v2-20220224", "Anaf/Schemas/D100/v2-20220224/d100_24022022.xsd.xml", "2026-09", new DateOnly(2025, 1, 1), null },
                    { D301, "D301", "v1-20200130", "Anaf/Schemas/D301/v1-20200130/d301_20200130.xsd.xml", "2026-09", new DateOnly(2025, 1, 1), null },
                    { D390, "D390", "v3-20210212", "Anaf/Schemas/D390/v3-20210212/d390_12022021.xsd.xml", "2026-09", new DateOnly(2025, 1, 1), null },
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(schema: "public", table: "anaf_declaration_schemas", keyColumn: "id", keyColumnType: "uuid", keyValues: [D100, D301, D390]);
        }
    }
}
