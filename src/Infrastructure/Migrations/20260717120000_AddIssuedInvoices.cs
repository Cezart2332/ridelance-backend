using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuedInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issued_invoices",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    client_cif = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    client_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    amount_bani = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    series_name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    link = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error_message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    is_test = table.Column<bool>(type: "boolean", nullable: false),
                    sent_to_spv = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issued_invoices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issued_invoices_payment_record_id",
                schema: "public",
                table: "issued_invoices",
                column: "payment_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_issued_invoices_service_order_id",
                schema: "public",
                table: "issued_invoices",
                column: "service_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_issued_invoices_user_id",
                schema: "public",
                table: "issued_invoices",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_issued_invoices_created_at_utc",
                schema: "public",
                table: "issued_invoices",
                column: "created_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issued_invoices",
                schema: "public");
        }
    }
}
