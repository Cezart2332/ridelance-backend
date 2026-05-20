using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddServiceOrders : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "service_orders",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                service_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                service_title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                customer_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                customer_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                customer_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                stripe_session_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                amount_bani = table.Column<long>(type: "bigint", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                paid_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_service_orders", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_service_orders_customer_email",
            schema: "public",
            table: "service_orders",
            column: "customer_email");

        migrationBuilder.CreateIndex(
            name: "ix_service_orders_stripe_session_id",
            schema: "public",
            table: "service_orders",
            column: "stripe_session_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "service_orders",
            schema: "public");
    }
}
