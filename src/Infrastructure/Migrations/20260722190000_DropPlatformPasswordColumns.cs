using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropPlatformPasswordColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "password_protected",
                schema: "public",
                table: "pfa_platform_accounts");

            migrationBuilder.DropColumn(
                name: "password_updated_at_utc",
                schema: "public",
                table: "pfa_platform_accounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "password_protected",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "password_updated_at_utc",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
