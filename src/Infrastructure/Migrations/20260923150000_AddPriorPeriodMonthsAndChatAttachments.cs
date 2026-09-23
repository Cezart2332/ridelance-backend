using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Lunile de dinainte de RIDElance, trecute de contabil pentru taxele estimate, și fișierele
    /// atașate în chat. Rulările oprite pe „date insuficiente” din cauza unei perioade lipsă se
    /// refac: acum perioada se estimează din media lunilor cunoscute.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddPriorPeriodMonthsAndChatAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pfa_prior_period_months",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    income = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    expenses = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                },
                constraints: table => table.PrimaryKey("pk_pfa_prior_period_months", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_pfa_prior_period_months_pfa_registration_id_year_month",
                schema: "public",
                table: "pfa_prior_period_months",
                columns: new[] { "pfa_registration_id", "year", "month" },
                unique: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_file_name",
                schema: "public",
                table: "chat_messages",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_content_type",
                schema: "public",
                table: "chat_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "attachment_size",
                schema: "public",
                table: "chat_messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_path",
                schema: "public",
                table: "chat_messages",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_iv",
                schema: "public",
                table: "chat_messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE public.fiscal_estimate_runs r
                SET stale = TRUE, stale_since_utc = NOW() - INTERVAL '1 hour'
                WHERE NOT r.stale
                  AND EXISTS (
                      SELECT 1 FROM public.fiscal_calculations c
                      WHERE c.run_id = r.id AND c.reason_code = 'COVERAGE_GAP');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "pfa_prior_period_months", schema: "public");
            migrationBuilder.DropColumn(name: "attachment_file_name", schema: "public", table: "chat_messages");
            migrationBuilder.DropColumn(name: "attachment_content_type", schema: "public", table: "chat_messages");
            migrationBuilder.DropColumn(name: "attachment_size", schema: "public", table: "chat_messages");
            migrationBuilder.DropColumn(name: "attachment_path", schema: "public", table: "chat_messages");
            migrationBuilder.DropColumn(name: "attachment_iv", schema: "public", table: "chat_messages");
        }
    }
}
