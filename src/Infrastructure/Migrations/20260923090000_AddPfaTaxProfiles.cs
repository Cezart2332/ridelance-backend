using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Profilul fiscal anual al PFA-ului: profilul, reviziile, cererile de corectare, reamintirile
    /// săptămânale și sarcinile de apel.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddPfaTaxProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pfa_tax_profiles",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_year = table.Column<int>(type: "integer", nullable: false),
                    regime = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    answers_json = table.Column<string>(type: "jsonb", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    first_prompt_shown_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    access_granted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pfa_registered_on = table.Column<DateOnly>(type: "date", nullable: true),
                    pfa_registered_on_source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pfa_registered_on_observed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    estimated_taxes_unlocked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_tax_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_tax_profiles_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pfa_tax_profiles_pfa_registration_id_tax_year",
                schema: "public",
                table: "pfa_tax_profiles",
                columns: new[] { "pfa_registration_id", "tax_year" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "pfa_tax_profile_revisions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    changes_json = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_tax_profile_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_tax_profile_revisions_pfa_tax_profiles_profile_id",
                        column: x => x.profile_id,
                        principalSchema: "public",
                        principalTable: "pfa_tax_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pfa_tax_profile_revisions_profile_id_revision",
                schema: "public",
                table: "pfa_tax_profile_revisions",
                columns: new[] { "profile_id", "revision" });

            migrationBuilder.CreateTable(
                name: "pfa_data_correction_requests",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fields = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table => table.PrimaryKey("pk_pfa_data_correction_requests", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_pfa_data_correction_requests_pfa_registration_id_state",
                schema: "public",
                table: "pfa_data_correction_requests",
                columns: new[] { "pfa_registration_id", "state" });

            migrationBuilder.CreateTable(
                name: "fiscal_profile_reminders",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_year = table.Column<int>(type: "integer", nullable: false),
                    week_index = table.Column<int>(type: "integer", nullable: false),
                    scheduled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table => table.PrimaryKey("pk_fiscal_profile_reminders", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_profile_reminders_pfa_registration_id_tax_year_week_index",
                schema: "public",
                table: "fiscal_profile_reminders",
                columns: new[] { "pfa_registration_id", "tax_year", "week_index" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "admin_call_tasks",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_year = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    call_outcome = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    rescheduled_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table => table.PrimaryKey("pk_admin_call_tasks", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_admin_call_tasks_pfa_registration_id_tax_year_reason",
                schema: "public",
                table: "admin_call_tasks",
                columns: new[] { "pfa_registration_id", "tax_year", "reason" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "admin_call_tasks", schema: "public");
            migrationBuilder.DropTable(name: "fiscal_profile_reminders", schema: "public");
            migrationBuilder.DropTable(name: "pfa_data_correction_requests", schema: "public");
            migrationBuilder.DropTable(name: "pfa_tax_profile_revisions", schema: "public");
            migrationBuilder.DropTable(name: "pfa_tax_profiles", schema: "public");
        }
    }
}
