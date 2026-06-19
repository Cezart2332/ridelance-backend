using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPfaNotesAndActivityLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "processed_at_utc",
                schema: "public",
                table: "pfa_monthly_incomes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "processed_by_user_id",
                schema: "public",
                table: "pfa_monthly_incomes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pfa_activity_logs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_type = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_activity_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_activity_logs_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pfa_activity_logs_users_performed_by_user_id",
                        column: x => x.performed_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pfa_internal_notes",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_internal_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_internal_notes_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pfa_internal_notes_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pfa_monthly_incomes_processed_by_user_id",
                schema: "public",
                table: "pfa_monthly_incomes",
                column: "processed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pfa_activity_logs_performed_by_user_id",
                schema: "public",
                table: "pfa_activity_logs",
                column: "performed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pfa_activity_logs_pfa_registration_id",
                schema: "public",
                table: "pfa_activity_logs",
                column: "pfa_registration_id");

            migrationBuilder.CreateIndex(
                name: "ix_pfa_internal_notes_created_by_user_id",
                schema: "public",
                table: "pfa_internal_notes",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pfa_internal_notes_pfa_registration_id",
                schema: "public",
                table: "pfa_internal_notes",
                column: "pfa_registration_id");

            migrationBuilder.AddForeignKey(
                name: "fk_pfa_monthly_incomes_users_processed_by_user_id",
                schema: "public",
                table: "pfa_monthly_incomes",
                column: "processed_by_user_id",
                principalSchema: "public",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pfa_monthly_incomes_users_processed_by_user_id",
                schema: "public",
                table: "pfa_monthly_incomes");

            migrationBuilder.DropTable(
                name: "pfa_activity_logs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "pfa_internal_notes",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "ix_pfa_monthly_incomes_processed_by_user_id",
                schema: "public",
                table: "pfa_monthly_incomes");

            migrationBuilder.DropColumn(
                name: "processed_at_utc",
                schema: "public",
                table: "pfa_monthly_incomes");

            migrationBuilder.DropColumn(
                name: "processed_by_user_id",
                schema: "public",
                table: "pfa_monthly_incomes");
        }
    }
}
