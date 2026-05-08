using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations;
    /// <inheritdoc />
    public partial class UpdateCarModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "experience",
                schema: "public",
                table: "car_leads");

            migrationBuilder.DropColumn(
                name: "message",
                schema: "public",
                table: "car_leads");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "experience",
                schema: "public",
                table: "car_leads",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "message",
                schema: "public",
                table: "car_leads",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }
    }
