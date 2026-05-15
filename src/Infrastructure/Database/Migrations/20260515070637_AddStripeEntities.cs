using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class AddStripeEntities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "payment_records",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                payment_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                amount_bani = table.Column<long>(type: "bigint", nullable: false),
                description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                stripe_payment_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                stripe_session_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_payment_records", x => x.id);
                table.ForeignKey(
                    name: "fk_payment_records_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_subscriptions",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                plan = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                stripe_subscription_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                stripe_customer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                first_billing_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                next_billing_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                cancelled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_subscriptions", x => x.id);
                table.ForeignKey(
                    name: "fk_user_subscriptions_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_payment_records_stripe_payment_id",
            schema: "public",
            table: "payment_records",
            column: "stripe_payment_id");

        migrationBuilder.CreateIndex(
            name: "ix_payment_records_user_id",
            schema: "public",
            table: "payment_records",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_user_subscriptions_stripe_customer_id",
            schema: "public",
            table: "user_subscriptions",
            column: "stripe_customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_user_subscriptions_stripe_subscription_id",
            schema: "public",
            table: "user_subscriptions",
            column: "stripe_subscription_id",
            unique: true,
            filter: "stripe_subscription_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_user_subscriptions_user_id",
            schema: "public",
            table: "user_subscriptions",
            column: "user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "payment_records",
            schema: "public");

        migrationBuilder.DropTable(
            name: "user_subscriptions",
            schema: "public");
    }
}
