using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// RL-02 — pasul fiscal se închide din admin.
    ///
    /// Trei lucruri: (1) semnalul „mi-am terminat partea” de la șofer plus datele pachetului alocat
    /// de admin, pe pachetul de semnături existent; (2) tabela de urmă a tranzițiilor de pas, care
    /// lipsea complet fiindcă statusul pașilor se derivă și nu se stochează nicăieri; (3) un
    /// backfill fără care dosarele care aveau deja pasul fiscal închis ar regresa în „în lucru”,
    /// pentru că de acum pasul cere și pachet de semnături finalizat.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddOnboardingStepAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_for_review_at_utc",
                schema: "public",
                table: "onboarding_signature_packets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "package_name",
                schema: "public",
                table: "onboarding_signature_packets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "signature_count",
                schema: "public",
                table: "onboarding_signature_packets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "expires_at_utc",
                schema: "public",
                table: "onboarding_signature_packets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                schema: "public",
                table: "onboarding_signature_packets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "onboarding_step_audits",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    to_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_step_audits", x => x.id);
                    table.ForeignKey(
                        name: "fk_onboarding_step_audits_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_onboarding_step_audits_users_performed_by_user_id",
                        column: x => x.performed_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_step_audits_pfa_registration_id_step_key",
                schema: "public",
                table: "onboarding_step_audits",
                columns: ["pfa_registration_id", "step_key"]);

            // Dosarele care aveau deja pasul fiscal închis după vechea regulă (bancă verificată +
            // consimțăminte Oblio + răspuns TVA) primesc un pachet finalizat retroactiv. Fără asta,
            // regula nouă i-ar trimite înapoi într-un pas pe care îl terminaseră.
            migrationBuilder.Sql("""
                INSERT INTO public.onboarding_signature_packets
                    (id, pfa_registration_id, provider, status, signed_at_utc,
                     submitted_for_review_at_utc, admin_note, created_at_utc, updated_at_utc)
                SELECT
                    gen_random_uuid(), r.id, 'Manual', 'Completed', now(), now(),
                    'Backfill RL-02: pasul fiscal era deja finalizat după regula anterioară.',
                    now(), now()
                FROM public.pfa_registrations r
                JOIN public.pfa_bank_account_declarations b ON b.pfa_registration_id = r.id
                JOIN public.pfa_oblio_accounts o ON o.pfa_registration_id = r.id
                JOIN public.pfa_fiscal_profiles f ON f.pfa_registration_id = r.id
                LEFT JOIN public.onboarding_signature_packets p ON p.pfa_registration_id = r.id
                WHERE p.id IS NULL
                  AND b.status = 'Verified'
                  AND f.vat_answer IN ('Yes', 'No')
                  AND o.account_creation_consent AND o.data_processing_consent
                  AND o.e_invoice_consent AND o.auto_invoicing_consent
                  AND o.ridelance_management_consent AND o.terms_accepted_consent;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "onboarding_step_audits", schema: "public");

            migrationBuilder.DropColumn(
                name: "rejection_reason", schema: "public", table: "onboarding_signature_packets");
            migrationBuilder.DropColumn(
                name: "expires_at_utc", schema: "public", table: "onboarding_signature_packets");
            migrationBuilder.DropColumn(
                name: "signature_count", schema: "public", table: "onboarding_signature_packets");
            migrationBuilder.DropColumn(
                name: "package_name", schema: "public", table: "onboarding_signature_packets");
            migrationBuilder.DropColumn(
                name: "submitted_for_review_at_utc", schema: "public", table: "onboarding_signature_packets");
        }
    }
}
