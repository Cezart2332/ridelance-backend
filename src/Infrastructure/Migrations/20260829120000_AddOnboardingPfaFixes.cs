using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Fix-urile de onboarding PFA, partea de schemă:
    ///
    /// 1. <c>identity_mismatch_note</c> pe dosarul de înființare — CNP-ul devine sursa de adevăr
    ///    pentru data nașterii și sex, iar ce a citit OCR-ul diferit se notează pentru admin în
    ///    loc să blocheze șoferul pe câmpul CNP.
    /// 2. <c>payment_confirmed_at_utc</c>, <c>sent_to_consulto_at_utc</c> și
    ///    <c>consulto_send_stripe_event_id</c> — plata devine condiție de trimitere, iar
    ///    trimiterea se face o singură dată, din webhook. Ultima coloană e cheia de dedupe:
    ///    Stripe reîncearcă evenimentele.
    /// 3. <c>driver_email</c> / <c>driver_phone</c> / <c>driver_external_id</c> pe conturile de
    ///    platformă — pasul cerea doar contul de flotă, deci contul de șofer (cel cu care se
    ///    conduce efectiv) lipsea cu totul din dosar.
    ///
    /// <c>status</c> se stochează ca text, deci valorile noi de enum (AwaitingPayment,
    /// PaymentConfirmed, SentToConsulto) nu cer nicio migrare de date. Dosarele vechi rămân pe
    /// <c>Submitted</c> și nu trec poarta de trimitere — corect: nu știm dacă au fost plătite.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddOnboardingPfaFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "identity_mismatch_note",
                schema: "public",
                table: "company_formation_requests",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "payment_confirmed_at_utc",
                schema: "public",
                table: "company_formation_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "sent_to_consulto_at_utc",
                schema: "public",
                table: "company_formation_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "consulto_send_stripe_event_id",
                schema: "public",
                table: "company_formation_requests",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_email",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_phone",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_external_id",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "identity_mismatch_note", schema: "public", table: "company_formation_requests");
            migrationBuilder.DropColumn(
                name: "payment_confirmed_at_utc", schema: "public", table: "company_formation_requests");
            migrationBuilder.DropColumn(
                name: "sent_to_consulto_at_utc", schema: "public", table: "company_formation_requests");
            migrationBuilder.DropColumn(
                name: "consulto_send_stripe_event_id", schema: "public", table: "company_formation_requests");
            migrationBuilder.DropColumn(
                name: "driver_email", schema: "public", table: "pfa_platform_accounts");
            migrationBuilder.DropColumn(
                name: "driver_phone", schema: "public", table: "pfa_platform_accounts");
            migrationBuilder.DropColumn(
                name: "driver_external_id", schema: "public", table: "pfa_platform_accounts");
        }
    }
}
