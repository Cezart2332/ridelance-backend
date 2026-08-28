using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Predarea, primirea și cronologia mașinii.
    ///
    /// O singură tabelă pentru ambele momente, discriminată prin `kind`: câmpurile sunt aceleași,
    /// iar două tabele identice ar fi însemnat două locuri în care se scrie același lucru.
    /// Indexul unic pe (închiriere, tip) ține câte una din fiecare — a doua predare ar face
    /// istoricul ambiguu.
    ///
    /// `vehicle_events` e append-only și se scrie exclusiv din handlere. Un istoric completat de
    /// utilizator nu mai e istoric, e o listă de afirmații.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddChecksAndTimeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "check_records",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rental_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    mileage = table.Column<int>(type: "integer", nullable: false),
                    fuel_level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    accessories = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    deposit_returned_bani = table.Column<long>(type: "bigint", nullable: true),
                    deposit_withheld_bani = table.Column<long>(type: "bigint", nullable: true),
                    withholding_reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    extra_mileage_charge_bani = table.Column<long>(type: "bigint", nullable: true),
                    other_charges_bani = table.Column<long>(type: "bigint", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_check_records_rentals_rental_id",
                        column: x => x.rental_id,
                        principalSchema: "public",
                        principalTable: "rentals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_check_records_rental_id_kind",
                schema: "public",
                table: "check_records",
                columns: ["rental_id", "kind"],
                unique: true);

            migrationBuilder.CreateTable(
                name: "check_photos",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    check_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_check_photos_check_records_check_record_id",
                        column: x => x.check_record_id,
                        principalSchema: "public",
                        principalTable: "check_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_check_photos_check_record_id",
                schema: "public",
                table: "check_photos",
                column: "check_record_id");

            migrationBuilder.CreateTable(
                name: "vehicle_events",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    car_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    rental_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("pk_vehicle_events", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_events_car_id_occurred_at_utc",
                schema: "public",
                table: "vehicle_events",
                columns: ["car_id", "occurred_at_utc"],
                descending: [false, true]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "vehicle_events", schema: "public");
            migrationBuilder.DropTable(name: "check_photos", schema: "public");
            migrationBuilder.DropTable(name: "check_records", schema: "public");
        }
    }
}
