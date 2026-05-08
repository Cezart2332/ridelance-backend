using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

    /// <inheritdoc />
    public partial class AddAssignedContabil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_contabil_id",
                schema: "public",
                table: "pfa_registrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pfa_registrations_assigned_contabil_id",
                schema: "public",
                table: "pfa_registrations",
                column: "assigned_contabil_id");

            migrationBuilder.AddForeignKey(
                name: "fk_pfa_registrations_users_assigned_contabil_id",
                schema: "public",
                table: "pfa_registrations",
                column: "assigned_contabil_id",
                principalSchema: "public",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pfa_registrations_users_assigned_contabil_id",
                schema: "public",
                table: "pfa_registrations");

            migrationBuilder.DropIndex(
                name: "ix_pfa_registrations_assigned_contabil_id",
                schema: "public",
                table: "pfa_registrations");

            migrationBuilder.DropColumn(
                name: "assigned_contabil_id",
                schema: "public",
                table: "pfa_registrations");
        }
    }
