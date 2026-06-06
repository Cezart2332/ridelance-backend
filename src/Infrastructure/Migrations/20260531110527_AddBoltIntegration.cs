using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBoltIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bolt_integrations",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    client_secret = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    company_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    access_token = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    token_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_fetched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_connected = table.Column<bool>(type: "boolean", nullable: false),
                    error_message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bolt_integrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_bolt_integrations_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bolt_orders",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    driver_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    driver_uuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    driver_phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    payment_method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    order_created_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    order_status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pickup_address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    destination_address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ride_distance = table.Column<double>(type: "double precision", nullable: false),
                    ride_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    net_earnings = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    tip = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    commission = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    vehicle_model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    vehicle_license_plate = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    order_finished_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bolt_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_bolt_orders_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bolt_integrations_user_id",
                schema: "public",
                table: "bolt_integrations",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bolt_orders_user_id_order_reference",
                schema: "public",
                table: "bolt_orders",
                columns: new[] { "user_id", "order_reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bolt_integrations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "bolt_orders",
                schema: "public");
        }
    }
}
