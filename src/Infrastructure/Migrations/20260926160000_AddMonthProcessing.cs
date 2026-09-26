using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Procesarea lunii (spec contabilitate B3): rezultatul pre-check-ului pe PFA și lună
    /// (`pfa_month_checks`; lipsa rândului înseamnă „neprocesat”) și suma în valută pe liniile de
    /// declarație, cerută de secțiunea 4.1 a D301.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddMonthProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pfa_month_checks",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    reasons_json = table.Column<string>(type: "jsonb", nullable: false),
                    checked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_month_checks", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_month_checks_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pfa_month_checks_pfa_registration_id_period",
                schema: "public",
                table: "pfa_month_checks",
                columns: ["pfa_registration_id", "period"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pfa_month_checks_period",
                schema: "public",
                table: "pfa_month_checks",
                column: "period");

            migrationBuilder.AddColumn<decimal>(
                name: "amount_in_currency",
                schema: "public",
                table: "declaration_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "amount_in_currency", schema: "public", table: "declaration_lines");
            migrationBuilder.DropTable(name: "pfa_month_checks", schema: "public");
        }
    }
}
