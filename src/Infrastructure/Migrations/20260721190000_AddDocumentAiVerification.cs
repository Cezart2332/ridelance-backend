using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentAiVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ai_status",
                schema: "public",
                table: "documents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "ai_summary",
                schema: "public",
                table: "documents",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ai_detected_type",
                schema: "public",
                table: "documents",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ai_extracted_expires_at_utc",
                schema: "public",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ai_processed_at_utc",
                schema: "public",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ai_attempts",
                schema: "public",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_status",
                schema: "public",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ai_summary",
                schema: "public",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ai_detected_type",
                schema: "public",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ai_extracted_expires_at_utc",
                schema: "public",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ai_processed_at_utc",
                schema: "public",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ai_attempts",
                schema: "public",
                table: "documents");
        }
    }
}
