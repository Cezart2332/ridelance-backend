using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations;

/// <inheritdoc />
    public partial class AddDashboardAccessGranted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "dashboard_access_granted",
                schema: "public",
                table: "user_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "dashboard_access_granted_utc",
                schema: "public",
                table: "user_subscriptions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dashboard_access_granted",
                schema: "public",
                table: "user_subscriptions");

            migrationBuilder.DropColumn(
                name: "dashboard_access_granted_utc",
                schema: "public",
                table: "user_subscriptions");
        }
    }
