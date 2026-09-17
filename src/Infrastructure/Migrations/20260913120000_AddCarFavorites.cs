using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// <c>car_favorites</c>: mașinile salvate la favorite de utilizatorii cu cont.
    ///
    /// Cine n-are cont își ține favoritele în browser; la logare ele se mută aici.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCarFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "car_favorites",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    car_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_car_favorites", x => x.id);
                    table.ForeignKey(
                        name: "fk_car_favorites_cars_car_id",
                        column: x => x.car_id,
                        principalSchema: "public",
                        principalTable: "cars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_car_favorites_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_car_favorites_user_id_car_id",
                schema: "public",
                table: "car_favorites",
                columns: ["user_id", "car_id"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_car_favorites_car_id",
                schema: "public",
                table: "car_favorites",
                column: "car_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "car_favorites", schema: "public");
        }
    }
}
