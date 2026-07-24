using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861
#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OnboardingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Coloane noi pe documents (OCR extins + legături adăugate progresiv) ---
            migrationBuilder.AddColumn<double>(
                name: "ai_confidence",
                schema: "public",
                table: "documents",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ai_extracted_json",
                schema: "public",
                table: "documents",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ai_requires_manual_review",
                schema: "public",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "pfa_vehicle_id",
                schema: "public",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "platform_provider",
                schema: "public",
                table: "documents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "issued_at_utc",
                schema: "public",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_document_id",
                schema: "public",
                table: "documents",
                type: "uuid",
                nullable: true);

            // --- app_settings (parametri comerciali/operaționali configurabili) ---
            migrationBuilder.CreateTable(
                name: "app_settings",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: false),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_settings_key",
                schema: "public",
                table: "app_settings",
                column: "key",
                unique: true);

            // --- extracted_fields (proveniența câmpurilor extrase prin OCR) ---
            migrationBuilder.CreateTable(
                name: "extracted_fields",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ai_value = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ai_normalized_value = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ai_confidence = table.Column<double>(type: "double precision", nullable: false),
                    validator_passed = table.Column<bool>(type: "boolean", nullable: false),
                    effective_confidence = table.Column<double>(type: "double precision", nullable: false),
                    confirmed_value = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    confirmed_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    change_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    review_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extracted_fields", x => x.id);
                    table.ForeignKey(
                        name: "fk_extracted_fields_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_extracted_fields_document_id_field_key",
                schema: "public",
                table: "extracted_fields",
                columns: new[] { "document_id", "field_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "extracted_fields", schema: "public");
            migrationBuilder.DropTable(name: "app_settings", schema: "public");

            migrationBuilder.DropColumn(name: "ai_confidence", schema: "public", table: "documents");
            migrationBuilder.DropColumn(name: "ai_extracted_json", schema: "public", table: "documents");
            migrationBuilder.DropColumn(name: "ai_requires_manual_review", schema: "public", table: "documents");
            migrationBuilder.DropColumn(name: "pfa_vehicle_id", schema: "public", table: "documents");
            migrationBuilder.DropColumn(name: "platform_provider", schema: "public", table: "documents");
            migrationBuilder.DropColumn(name: "issued_at_utc", schema: "public", table: "documents");
            migrationBuilder.DropColumn(name: "replaced_by_document_id", schema: "public", table: "documents");
        }
    }
}
