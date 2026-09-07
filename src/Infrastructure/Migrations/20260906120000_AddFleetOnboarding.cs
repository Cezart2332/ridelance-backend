using Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260906120000_AddFleetOnboarding")]
public sealed class AddFleetOnboarding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("fleet_onboarding_required", "users", "boolean", schema: "public", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>("fleet_onboarding", "users", "jsonb", schema: "public", nullable: false, defaultValue: "{}");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("fleet_onboarding_required", "users", "public");
        migrationBuilder.DropColumn("fleet_onboarding", "users", "public");
    }
}
