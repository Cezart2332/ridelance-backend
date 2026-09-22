using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Motorul de taxe estimate: rulările și rezultatele lor pe componente, plus suma pe care PFA-ul
    /// spune că o are deja pusă deoparte.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddFiscalEstimates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "existing_reserve",
                schema: "public",
                table: "pfa_tax_profiles",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fiscal_estimate_runs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_year = table.Column<int>(type: "integer", nullable: false),
                    profile_revision = table.Column<int>(type: "integer", nullable: false),
                    rule_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    financial_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    assumptions_json = table.Column<string>(type: "jsonb", nullable: false),
                    missing_inputs_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    stale = table.Column<bool>(type: "boolean", nullable: false),
                    stale_since_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("pk_fiscal_estimate_runs", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_estimate_runs_pfa_registration_id_tax_year_created_at_utc",
                schema: "public",
                table: "fiscal_estimate_runs",
                columns: new[] { "pfa_registration_id", "tax_year", "created_at_utc" });

            migrationBuilder.CreateTable(
                name: "fiscal_calculations",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    missing_inputs_json = table.Column<string>(type: "jsonb", nullable: false),
                    breakdown_json = table.Column<string>(type: "jsonb", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_calculations", x => x.id);
                    table.ForeignKey(
                        name: "fk_fiscal_calculations_fiscal_estimate_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "public",
                        principalTable: "fiscal_estimate_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_calculations_run_id",
                schema: "public",
                table: "fiscal_calculations",
                column: "run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "fiscal_calculations", schema: "public");
            migrationBuilder.DropTable(name: "fiscal_estimate_runs", schema: "public");
            migrationBuilder.DropColumn(name: "existing_reserve", schema: "public", table: "pfa_tax_profiles");
        }
    }
}
