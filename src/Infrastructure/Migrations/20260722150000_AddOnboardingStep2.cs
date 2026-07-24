using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingStep2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Pasul 2.1: TVA pe pfa_fiscal_profiles ---
            migrationBuilder.AddColumn<string>(
                name: "vat_answer",
                schema: "public",
                table: "pfa_fiscal_profiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "vat_registration_kind",
                schema: "public",
                table: "pfa_fiscal_profiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            // --- Pasul 2.2: pachet de semnături ---
            migrationBuilder.CreateTable(
                name: "onboarding_signature_packets",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    signed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    admin_note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_signature_packets", x => x.id);
                    table.ForeignKey(
                        name: "fk_onboarding_signature_packets_pfa_registrations_pfa_registra",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_signature_packets_pfa_registration_id",
                schema: "public",
                table: "onboarding_signature_packets",
                column: "pfa_registration_id",
                unique: true);

            migrationBuilder.CreateTable(
                name: "onboarding_signature_documents",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    packet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_signed = table.Column<bool>(type: "boolean", nullable: false),
                    signed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_signature_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_onboarding_signature_documents_onboarding_signature_packet",
                        column: x => x.packet_id,
                        principalSchema: "public",
                        principalTable: "onboarding_signature_packets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_signature_documents_packet_id",
                schema: "public",
                table: "onboarding_signature_documents",
                column: "packet_id");

            // --- Pasul 2.3: declarația contului bancar ---
            migrationBuilder.CreateTable(
                name: "pfa_bank_account_declarations",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    iban_encrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    iban_masked = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    confirmation_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ocr_iban_matches = table.Column<bool>(type: "boolean", nullable: true),
                    bank_connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    admin_note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_bank_account_declarations", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_bank_account_declarations_pfa_registrations_pfa_registr",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pfa_bank_account_declarations_pfa_registration_id",
                schema: "public",
                table: "pfa_bank_account_declarations",
                column: "pfa_registration_id",
                unique: true);

            // --- Pasul 2.4: contul Oblio ---
            migrationBuilder.CreateTable(
                name: "pfa_oblio_accounts",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    account_creation_consent = table.Column<bool>(type: "boolean", nullable: false),
                    data_processing_consent = table.Column<bool>(type: "boolean", nullable: false),
                    e_invoice_consent = table.Column<bool>(type: "boolean", nullable: false),
                    auto_invoicing_consent = table.Column<bool>(type: "boolean", nullable: false),
                    ridelance_management_consent = table.Column<bool>(type: "boolean", nullable: false),
                    terms_accepted_consent = table.Column<bool>(type: "boolean", nullable: false),
                    consent_text_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    consents_accepted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    integration_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    admin_note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_oblio_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_oblio_accounts_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pfa_oblio_accounts_pfa_registration_id",
                schema: "public",
                table: "pfa_oblio_accounts",
                column: "pfa_registration_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "pfa_oblio_accounts", schema: "public");
            migrationBuilder.DropTable(name: "pfa_bank_account_declarations", schema: "public");
            migrationBuilder.DropTable(name: "onboarding_signature_documents", schema: "public");
            migrationBuilder.DropTable(name: "onboarding_signature_packets", schema: "public");

            migrationBuilder.DropColumn(name: "vat_answer", schema: "public", table: "pfa_fiscal_profiles");
            migrationBuilder.DropColumn(name: "vat_registration_kind", schema: "public", table: "pfa_fiscal_profiles");
        }
    }
}
