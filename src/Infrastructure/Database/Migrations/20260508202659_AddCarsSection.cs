using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations;
    /// <inheritdoc />
    public partial class AddCarsSection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cars",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    model = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    engine = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    transmission = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    location = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    price_per_week = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    old_price = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    discount_active = table.Column<bool>(type: "boolean", nullable: false),
                    offer_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    uber_categories = table.Column<string>(type: "jsonb", nullable: false),
                    bolt_categories = table.Column<string>(type: "jsonb", nullable: false),
                    badges = table.Column<string>(type: "jsonb", nullable: false),
                    description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    conditions = table.Column<string>(type: "jsonb", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cars", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "car_images",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    car_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_car_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_car_images_cars_car_id",
                        column: x => x.car_id,
                        principalSchema: "public",
                        principalTable: "cars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "car_leads",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    car_id = table.Column<Guid>(type: "uuid", nullable: false),
                    car_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    user_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    user_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    experience = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    interest_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    admin_note = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_car_leads", x => x.id);
                    table.ForeignKey(
                        name: "fk_car_leads_cars_car_id",
                        column: x => x.car_id,
                        principalSchema: "public",
                        principalTable: "cars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_car_images_car_id",
                schema: "public",
                table: "car_images",
                column: "car_id");

            migrationBuilder.CreateIndex(
                name: "ix_car_leads_car_id",
                schema: "public",
                table: "car_leads",
                column: "car_id");

            migrationBuilder.CreateIndex(
                name: "ix_car_leads_status",
                schema: "public",
                table: "car_leads",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "car_images",
                schema: "public");

            migrationBuilder.DropTable(
                name: "car_leads",
                schema: "public");

            migrationBuilder.DropTable(
                name: "cars",
                schema: "public");
        }
    }
