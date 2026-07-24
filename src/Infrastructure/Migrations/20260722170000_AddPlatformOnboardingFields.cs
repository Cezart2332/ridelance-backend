using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformOnboardingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_selected_by_user",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_existing_account",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "operator_account_id",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "affiliation_contract_document_id",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "onboarding_status",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotStarted");

            // Scrub: parolele nu se mai stochează.
            migrationBuilder.Sql(
                "UPDATE public.pfa_platform_accounts SET password_protected = NULL, password_updated_at_utc = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "is_selected_by_user", schema: "public", table: "pfa_platform_accounts");
            migrationBuilder.DropColumn(name: "has_existing_account", schema: "public", table: "pfa_platform_accounts");
            migrationBuilder.DropColumn(name: "operator_account_id", schema: "public", table: "pfa_platform_accounts");
            migrationBuilder.DropColumn(name: "affiliation_contract_document_id", schema: "public", table: "pfa_platform_accounts");
            migrationBuilder.DropColumn(name: "onboarding_status", schema: "public", table: "pfa_platform_accounts");
        }
    }
}
