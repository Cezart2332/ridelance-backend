using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddReviewedByUserId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "reviewed_by_user_id",
            schema: "public",
            table: "pfa_registrations",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_pfa_registrations_reviewed_by_user_id",
            schema: "public",
            table: "pfa_registrations",
            column: "reviewed_by_user_id");

        migrationBuilder.AddForeignKey(
            name: "fk_pfa_registrations_users_reviewed_by_user_id",
            schema: "public",
            table: "pfa_registrations",
            column: "reviewed_by_user_id",
            principalSchema: "public",
            principalTable: "users",
            principalColumn: "id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_pfa_registrations_users_reviewed_by_user_id",
            schema: "public",
            table: "pfa_registrations");

        migrationBuilder.DropIndex(
            name: "ix_pfa_registrations_reviewed_by_user_id",
            schema: "public",
            table: "pfa_registrations");

        migrationBuilder.DropColumn(
            name: "reviewed_by_user_id",
            schema: "public",
            table: "pfa_registrations");
    }
}
