using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // generated migration code

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficeCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "office_appointments",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_office_appointments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "office_blocked_slots",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_office_blocked_slots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "office_schedule_days",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    open_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    close_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_office_schedule_days", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_office_appointments_date",
                schema: "public",
                table: "office_appointments",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_office_appointments_date_start_time",
                schema: "public",
                table: "office_appointments",
                columns: new[] { "date", "start_time" },
                unique: true,
                filter: "status = 'Confirmed'");

            migrationBuilder.CreateIndex(
                name: "ix_office_blocked_slots_date",
                schema: "public",
                table: "office_blocked_slots",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_office_schedule_days_day",
                schema: "public",
                table: "office_schedule_days",
                column: "day",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "office_appointments",
                schema: "public");

            migrationBuilder.DropTable(
                name: "office_blocked_slots",
                schema: "public");

            migrationBuilder.DropTable(
                name: "office_schedule_days",
                schema: "public");
        }
    }
}
