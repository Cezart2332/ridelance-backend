using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPfaFiscalProfile : Migration
    {
        private static readonly string[] PlatformAccountUniqueIndexColumns = ["pfa_registration_id", "provider", "kind"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pfa_fiscal_profiles",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taxation_system = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_vat_payer = table.Column<bool>(type: "boolean", nullable: false),
                    has_employees = table.Column<bool>(type: "boolean", nullable: false),
                    accounting_regime = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    special_vat_code_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    special_vat_code_obtained_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    special_vat_code_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uber_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bolt_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    other_platforms_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cash_revenue_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cash_register_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    vehicle_usage_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    vehicle_supporting_document_label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    vehicle_supporting_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_fiscal_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_fiscal_profiles_documents_special_vat_code_document_id",
                        column: x => x.special_vat_code_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pfa_fiscal_profiles_documents_vehicle_supporting_document_id",
                        column: x => x.vehicle_supporting_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pfa_fiscal_profiles_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pfa_fleet_consents",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fleet_accounts_accepted = table.Column<bool>(type: "boolean", nullable: false),
                    fleet_accounts_accepted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    bolt_api_accepted = table.Column<bool>(type: "boolean", nullable: false),
                    bolt_api_accepted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    consent_text_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_fleet_consents", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_fleet_consents_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pfa_platform_accounts",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    password_protected = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    password_updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    configured_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_platform_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_platform_accounts_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pfa_fiscal_profiles_pfa_registration_id",
                schema: "public",
                table: "pfa_fiscal_profiles",
                column: "pfa_registration_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pfa_fiscal_profiles_special_vat_code_document_id",
                schema: "public",
                table: "pfa_fiscal_profiles",
                column: "special_vat_code_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_pfa_fiscal_profiles_vehicle_supporting_document_id",
                schema: "public",
                table: "pfa_fiscal_profiles",
                column: "vehicle_supporting_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_pfa_fleet_consents_pfa_registration_id",
                schema: "public",
                table: "pfa_fleet_consents",
                column: "pfa_registration_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pfa_platform_accounts_pfa_registration_id_provider_kind",
                schema: "public",
                table: "pfa_platform_accounts",
                columns: PlatformAccountUniqueIndexColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pfa_fiscal_profiles",
                schema: "public");

            migrationBuilder.DropTable(
                name: "pfa_fleet_consents",
                schema: "public");

            migrationBuilder.DropTable(
                name: "pfa_platform_accounts",
                schema: "public");
        }
    }
}
