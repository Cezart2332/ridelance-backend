using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Reîncercările de încasare a abonamentelor: factura reîncercată, câte încercări, următoarea și
    /// motivul ultimului refuz pe abonament; pe fiecare plată eșuată, factura, a câta încercare a fost
    /// și de ce a refuzat banca.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddSubscriptionPaymentRetry : Migration
    {
        private const string Schema = "public";
        private const string Subscriptions = "user_subscriptions";
        private const string Payments = "payment_records";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "payment_retry_invoice_id", schema: Schema, table: Subscriptions, type: "character varying(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<string>(name: "payment_retry_invoice_url", schema: Schema, table: Subscriptions, type: "character varying(1024)", maxLength: 1024, nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "payment_retry_started_at_utc", schema: Schema, table: Subscriptions, type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<int>(name: "payment_retry_attempts", schema: Schema, table: Subscriptions, type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<DateTime>(name: "next_payment_retry_at_utc", schema: Schema, table: Subscriptions, type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<string>(name: "last_payment_failure_reason", schema: Schema, table: Subscriptions, type: "character varying(512)", maxLength: 512, nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_subscriptions_next_payment_retry_at_utc",
                schema: Schema,
                table: Subscriptions,
                column: "next_payment_retry_at_utc",
                filter: "next_payment_retry_at_utc IS NOT NULL");

            migrationBuilder.AddColumn<string>(name: "stripe_invoice_id", schema: Schema, table: Payments, type: "character varying(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<int>(name: "attempt_number", schema: Schema, table: Payments, type: "integer", nullable: true);
            migrationBuilder.AddColumn<string>(name: "failure_code", schema: Schema, table: Payments, type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "failure_reason", schema: Schema, table: Payments, type: "character varying(512)", maxLength: 512, nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_records_stripe_invoice_id_attempt_number",
                schema: Schema,
                table: Payments,
                columns: ["stripe_invoice_id", "attempt_number"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ix_payment_records_stripe_invoice_id_attempt_number", schema: Schema, table: Payments);
            migrationBuilder.DropColumn(name: "failure_reason", schema: Schema, table: Payments);
            migrationBuilder.DropColumn(name: "failure_code", schema: Schema, table: Payments);
            migrationBuilder.DropColumn(name: "attempt_number", schema: Schema, table: Payments);
            migrationBuilder.DropColumn(name: "stripe_invoice_id", schema: Schema, table: Payments);

            migrationBuilder.DropIndex(name: "ix_user_subscriptions_next_payment_retry_at_utc", schema: Schema, table: Subscriptions);
            migrationBuilder.DropColumn(name: "last_payment_failure_reason", schema: Schema, table: Subscriptions);
            migrationBuilder.DropColumn(name: "next_payment_retry_at_utc", schema: Schema, table: Subscriptions);
            migrationBuilder.DropColumn(name: "payment_retry_attempts", schema: Schema, table: Subscriptions);
            migrationBuilder.DropColumn(name: "payment_retry_started_at_utc", schema: Schema, table: Subscriptions);
            migrationBuilder.DropColumn(name: "payment_retry_invoice_url", schema: Schema, table: Subscriptions);
            migrationBuilder.DropColumn(name: "payment_retry_invoice_id", schema: Schema, table: Subscriptions);
        }
    }
}
