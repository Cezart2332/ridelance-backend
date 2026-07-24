using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleStep5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pfa_vehicles",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ownership_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    add_later = table.Column<bool>(type: "boolean", nullable: false),
                    plate_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    vin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    make = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    first_registration_year = table.Column<int>(type: "integer", nullable: true),
                    marketplace_car_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_vehicles", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_vehicles_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_copy_requests",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    years = table.Column<int>(type: "integer", nullable: false),
                    fee_per_year_snapshot_bani = table.Column<long>(type: "bigint", nullable: false),
                    total_fee_snapshot_bani = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    dossier_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dossier_generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    copy_conforma_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    copy_conforma_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    admin_note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_copy_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_vehicle_copy_requests_pfa_vehicles_pfa_vehicle_id",
                        column: x => x.pfa_vehicle_id,
                        principalSchema: "public",
                        principalTable: "pfa_vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_badges",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    set_count = table.Column<int>(type: "integer", nullable: false),
                    fee_per_set_snapshot_bani = table.Column<long>(type: "bigint", nullable: false),
                    total_fee_snapshot_bani = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    badge_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_badges", x => x.id);
                    table.ForeignKey(
                        name: "fk_vehicle_badges_pfa_vehicles_pfa_vehicle_id",
                        column: x => x.pfa_vehicle_id,
                        principalSchema: "public",
                        principalTable: "pfa_vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pfa_vehicles_pfa_registration_id",
                schema: "public",
                table: "pfa_vehicles",
                column: "pfa_registration_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_copy_requests_pfa_vehicle_id",
                schema: "public",
                table: "vehicle_copy_requests",
                column: "pfa_vehicle_id",
                unique: true);

#pragma warning disable CA1861
            migrationBuilder.CreateIndex(
                name: "ix_vehicle_badges_pfa_vehicle_id_provider",
                schema: "public",
                table: "vehicle_badges",
                columns: new[] { "pfa_vehicle_id", "provider" },
                unique: true);
#pragma warning restore CA1861
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "vehicle_badges", schema: "public");
            migrationBuilder.DropTable(name: "vehicle_copy_requests", schema: "public");
            migrationBuilder.DropTable(name: "pfa_vehicles", schema: "public");
        }
    }
}
