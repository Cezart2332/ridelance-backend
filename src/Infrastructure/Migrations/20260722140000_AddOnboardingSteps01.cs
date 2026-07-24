using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingSteps01 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Coloane noi pe pfa_registrations (date PFA din Pasul 1) ---
            migrationBuilder.AddColumn<string>(
                name: "pfa_source",
                schema: "public",
                table: "pfa_registrations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Existing");

            migrationBuilder.AddColumn<string>(
                name: "legal_name",
                schema: "public",
                table: "pfa_registrations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registry_number",
                schema: "public",
                table: "pfa_registrations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "caen_codes",
                schema: "public",
                table: "pfa_registrations",
                type: "jsonb",
                nullable: true);

            // „Nu am PFA" existente = înființare via partener.
            migrationBuilder.Sql(
                "UPDATE public.pfa_registrations SET pfa_source = 'ViaPartner' WHERE registration_type = 'NuAmPfa';");

            // --- Pasul 0: onboarding_eligibility_profiles (legat de user) ---
            migrationBuilder.CreateTable(
                name: "onboarding_eligibility_profiles",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    id_series_mask = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    id_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_b_obtained_on = table.Column<DateOnly>(type: "date", nullable: true),
                    driving_categories = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    driving_licence_expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    driving_licence_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    has_driver_certificate = table.Column<bool>(type: "boolean", nullable: false),
                    driver_certificate_expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    driver_certificate_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_eligibility_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_onboarding_eligibility_profiles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_eligibility_profiles_user_id",
                schema: "public",
                table: "onboarding_eligibility_profiles",
                column: "user_id",
                unique: true);

            // --- Pasul 1 (Nu am PFA): pfa_partner_leads (Consulto) ---
            migrationBuilder.CreateTable(
                name: "pfa_partner_leads",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    county = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    housing_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    data_sharing_consent = table.Column<bool>(type: "boolean", nullable: false),
                    data_sharing_consent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    admin_note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_partner_leads", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_partner_leads_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pfa_partner_leads_pfa_registration_id",
                schema: "public",
                table: "pfa_partner_leads",
                column: "pfa_registration_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "pfa_partner_leads", schema: "public");
            migrationBuilder.DropTable(name: "onboarding_eligibility_profiles", schema: "public");

            migrationBuilder.DropColumn(name: "pfa_source", schema: "public", table: "pfa_registrations");
            migrationBuilder.DropColumn(name: "legal_name", schema: "public", table: "pfa_registrations");
            migrationBuilder.DropColumn(name: "registry_number", schema: "public", table: "pfa_registrations");
            migrationBuilder.DropColumn(name: "caen_codes", schema: "public", table: "pfa_registrations");
        }
    }
}
