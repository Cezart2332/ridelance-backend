using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Numărul de înmatriculare ascuns în anunț, plătit o dată per mașină.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCarHiddenPlate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "plate_hidden",
                schema: "public",
                table: "cars",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "plate_hidden_paid_at_utc",
                schema: "public",
                table: "cars",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "plate_hidden", schema: "public", table: "cars");
            migrationBuilder.DropColumn(name: "plate_hidden_paid_at_utc", schema: "public", table: "cars");
        }
    }
}
