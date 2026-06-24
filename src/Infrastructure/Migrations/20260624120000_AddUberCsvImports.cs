using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUberCsvImports : Migration
    {
        private static readonly string[] PeriodIndexColumns = ["pfa_registration_id", "year", "month"];
        private static readonly string[] UniqueImportIndexColumns = ["pfa_registration_id", "year", "month", "file_type", "file_name"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "uber_csv_imports",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    file_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    imported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    net_earnings = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    gross_earnings = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cash_collected = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    commission = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    trips = table.Column<int>(type: "integer", nullable: false),
                    kilometers = table.Column<double>(type: "double precision", nullable: false),
                    online_hours = table.Column<double>(type: "double precision", nullable: false),
                    ride_hours = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uber_csv_imports", x => x.id);
                    table.ForeignKey(
                        name: "fk_uber_csv_imports_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_uber_csv_imports_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_uber_csv_imports_pfa_registration_id_year_month",
                schema: "public",
                table: "uber_csv_imports",
                columns: PeriodIndexColumns);

            migrationBuilder.CreateIndex(
                name: "ix_uber_csv_imports_pfa_registration_id_year_month_file_type_f",
                schema: "public",
                table: "uber_csv_imports",
                columns: UniqueImportIndexColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_uber_csv_imports_user_id",
                schema: "public",
                table: "uber_csv_imports",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "uber_csv_imports",
                schema: "public");
        }
    }
}
