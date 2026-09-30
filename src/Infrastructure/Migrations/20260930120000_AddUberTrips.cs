using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Cursele Uber, rând cu rând. Până acum raportul de curse se reducea la „câte curse, câți km”
/// pe luna importului, iar rândurile se aruncau — istoricul curselor nu avea ce lista pentru Uber.
///
/// Importurile făcute înainte nu au curse salvate; reîncărcarea aceluiași CSV le completează.
/// </summary>
public partial class AddUberTrips : Migration
{
    private static readonly string[] UniqueTripColumns = ["pfa_registration_id", "trip_uuid"];
    private static readonly string[] UserDateColumns = ["user_id", "requested_at_utc"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "uber_trips",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                uber_csv_import_id = table.Column<Guid>(type: "uuid", nullable: false),
                trip_uuid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                dropped_off_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                pickup_address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                destination_address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                distance_km = table.Column<double>(type: "double precision", nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                product_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                payment_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_uber_trips", x => x.id);
                table.ForeignKey(
                    name: "fk_uber_trips_pfa_registrations_pfa_registration_id",
                    column: x => x.pfa_registration_id,
                    principalSchema: "public",
                    principalTable: "pfa_registrations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_uber_trips_uber_csv_imports_uber_csv_import_id",
                    column: x => x.uber_csv_import_id,
                    principalSchema: "public",
                    principalTable: "uber_csv_imports",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_uber_trips_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_uber_trips_pfa_registration_id_trip_uuid",
            schema: "public",
            table: "uber_trips",
            columns: UniqueTripColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_uber_trips_user_id_requested_at_utc",
            schema: "public",
            table: "uber_trips",
            columns: UserDateColumns);

        migrationBuilder.CreateIndex(
            name: "ix_uber_trips_uber_csv_import_id",
            schema: "public",
            table: "uber_trips",
            column: "uber_csv_import_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "uber_trips",
            schema: "public");
    }
}
