using Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260907120000_NotificationActions")]
public sealed class NotificationActions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("is_dismissed", "notifications", "boolean", schema: "public", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<Guid>("related_user_id", "notifications", "uuid", schema: "public", nullable: true);
        migrationBuilder.AddColumn<string>("section_key", "notifications", "character varying(64)", schema: "public", maxLength: 64, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("is_dismissed", "notifications", "public");
        migrationBuilder.DropColumn("related_user_id", "notifications", "public");
        migrationBuilder.DropColumn("section_key", "notifications", "public");
    }
}
