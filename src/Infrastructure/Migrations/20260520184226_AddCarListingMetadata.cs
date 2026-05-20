using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddCarListingMetadata : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "approval_status",
            schema: "public",
            table: "cars",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Approved");

        migrationBuilder.AddColumn<string>(
            name: "listing_source",
            schema: "public",
            table: "cars",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Ridelance");

        migrationBuilder.AddColumn<Guid>(
            name: "posted_by_user_id",
            schema: "public",
            table: "cars",
            type: "uuid",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "approval_status",
            schema: "public",
            table: "cars");

        migrationBuilder.DropColumn(
            name: "listing_source",
            schema: "public",
            table: "cars");

        migrationBuilder.DropColumn(
            name: "posted_by_user_id",
            schema: "public",
            table: "cars");
    }
}
