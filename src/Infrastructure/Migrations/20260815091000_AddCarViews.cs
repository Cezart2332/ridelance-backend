using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Vizualizările anunțurilor, ca rânduri, nu doar ca număr (spec §18).
    ///
    /// Contorul `view_count` exista deja, dar era incrementat la randarea fiecărui card din listă,
    /// deci număra afișări, nu vizite. De aici încolo se scrie doar din pagina de detaliu, o dată
    /// la 30 de minute per vizitator. Istoricul vechi rămâne pe loc: nu-l putem reface, dar nici
    /// nu-l ștergem — cifrele vor scădea brusc după deploy, ceea ce e corect, nu o regresie.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCarViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "unique_view_count",
                schema: "public",
                table: "cars",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "car_views",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    car_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visitor_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_car_views", x => x.id);
                    table.ForeignKey(
                        name: "fk_car_views_cars_car_id",
                        column: x => x.car_id,
                        principalSchema: "public",
                        principalTable: "cars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_car_views_car_id_created_at_utc",
                schema: "public",
                table: "car_views",
                columns: ["car_id", "created_at_utc"]);

            migrationBuilder.CreateIndex(
                name: "ix_car_views_car_id_visitor_hash_created_at_utc",
                schema: "public",
                table: "car_views",
                columns: ["car_id", "visitor_hash", "created_at_utc"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "car_views", schema: "public");
            migrationBuilder.DropColumn(name: "unique_view_count", schema: "public", table: "cars");
        }
    }
}
