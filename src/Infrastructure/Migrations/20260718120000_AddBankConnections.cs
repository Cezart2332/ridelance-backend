using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861
#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBankConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_connections",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    institution_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    institution_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    institution_logo_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    provider_requisition_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    provider_agreement_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    consent_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    expiry_notified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    max_historical_days = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    linked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_connections_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    iban_masked = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    owner_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_transactions_synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rate_limited_until_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_accounts_bank_connections_bank_connection_id",
                        column: x => x.bank_connection_id,
                        principalSchema: "public",
                        principalTable: "bank_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_transactions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_transaction_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    booking_date = table.Column<DateOnly>(type: "date", nullable: true),
                    value_date = table.Column<DateOnly>(type: "date", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    counterparty_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    remittance_info = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    is_pending = table.Column<bool>(type: "boolean", nullable: false),
                    raw_json = table.Column<string>(type: "jsonb", nullable: true),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    matched_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    matched_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    classified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    imported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_transactions_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalSchema: "public",
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_connections_reference",
                schema: "public",
                table: "bank_connections",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_connections_user_id",
                schema: "public",
                table: "bank_connections",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_bank_connection_id",
                schema: "public",
                table: "bank_accounts",
                column: "bank_connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_provider_account_id",
                schema: "public",
                table: "bank_accounts",
                column: "provider_account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_user_id",
                schema: "public",
                table: "bank_accounts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_bank_account_id_provider_transaction_id",
                schema: "public",
                table: "bank_transactions",
                columns: new[] { "bank_account_id", "provider_transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_user_id_booking_date",
                schema: "public",
                table: "bank_transactions",
                columns: new[] { "user_id", "booking_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_transactions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "bank_accounts",
                schema: "public");

            migrationBuilder.DropTable(
                name: "bank_connections",
                schema: "public");
        }
    }
}
