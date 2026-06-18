using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCarListingPaymentStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "paid_at_utc",
                schema: "public",
                table: "cars",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_status",
                schema: "public",
                table: "cars",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.AddColumn<string>(
                name: "stripe_checkout_session_id",
                schema: "public",
                table: "cars",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stripe_subscription_id",
                schema: "public",
                table: "cars",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_cars_stripe_checkout_session_id",
                schema: "public",
                table: "cars",
                column: "stripe_checkout_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_cars_stripe_subscription_id",
                schema: "public",
                table: "cars",
                column: "stripe_subscription_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cars_stripe_checkout_session_id",
                schema: "public",
                table: "cars");

            migrationBuilder.DropIndex(
                name: "ix_cars_stripe_subscription_id",
                schema: "public",
                table: "cars");

            migrationBuilder.DropColumn(
                name: "paid_at_utc",
                schema: "public",
                table: "cars");

            migrationBuilder.DropColumn(
                name: "payment_status",
                schema: "public",
                table: "cars");

            migrationBuilder.DropColumn(
                name: "stripe_checkout_session_id",
                schema: "public",
                table: "cars");

            migrationBuilder.DropColumn(
                name: "stripe_subscription_id",
                schema: "public",
                table: "cars");
        }
    }
}
