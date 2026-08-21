using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// <c>maintenance_entries</c> — istoricul de service al mașinilor din flotă.
    ///
    /// <c>owner_user_id</c> e denormalizat lângă <c>car_id</c>: lista „mentenanța flotei mele" e
    /// cea deschisă cel mai des, iar fără el fiecare afișare ar fi cerut un join cu <c>cars</c>
    /// doar ca să afle cine e proprietarul.
    ///
    /// Ștergerea mașinii duce istoricul cu ea — o intervenție fără mașină nu înseamnă nimic.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddMaintenanceEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_entries",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    car_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    performed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    mileage = table.Column<int>(type: "integer", nullable: true),
                    cost_bani = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    reminder_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reminder_mileage = table.Column<int>(type: "integer", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_entries_cars_car_id",
                        column: x => x.car_id,
                        principalSchema: "public",
                        principalTable: "cars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_maintenance_entries_owner_user_id_performed_at_utc
                    ON public.maintenance_entries (owner_user_id, performed_at_utc DESC);

                CREATE INDEX ix_maintenance_entries_car_id_performed_at_utc
                    ON public.maintenance_entries (car_id, performed_at_utc DESC);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "maintenance_entries", schema: "public");
        }
    }
}
