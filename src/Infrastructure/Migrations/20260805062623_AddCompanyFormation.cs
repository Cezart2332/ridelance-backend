using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Dosarul de înființare a societății prin partener, pentru ramura „Nu am PFA":
    /// datele solicitantului, sediul social, proprietarii imobilului, consimțămintele
    /// versionate și semnătura cu probatoriu de audit.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect: scaffolderul reconstruiește și
    /// obiecte create deja de migrațiile anterioare (snapshot-ul era în urmă).
    /// </summary>
    public partial class AddCompanyFormation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Valoarea în clar a unui câmp sensibil (CNP, serie/număr act) — criptată. În
            // ai_value / confirmed_value rămâne doar masca de afișare.
            migrationBuilder.AddColumn<string>(
                name: "encrypted_value",
                schema: "public",
                table: "extracted_fields",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "consulto_offices",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    adresa_judet = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    adresa_localitate = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    adresa_strada = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    adresa_numar = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    adresa_bloc = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    adresa_scara = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    adresa_etaj = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    adresa_apartament = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    monthly_fee_bani = table.Column<int>(type: "integer", nullable: false),
                    yearly_fee_bani = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consulto_offices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "legal_consent_flows",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    context = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_consent_flows", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "company_formation_requests",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    current_stage = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    solicitant_nume = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    solicitant_prenume = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    solicitant_cnp_encrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    solicitant_cnp_masked = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    solicitant_tip_act = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    solicitant_serie_act = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    solicitant_numar_act = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    solicitant_autoritate_emitenta = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    solicitant_data_emiterii = table.Column<DateOnly>(type: "date", nullable: true),
                    solicitant_data_expirarii = table.Column<DateOnly>(type: "date", nullable: true),
                    solicitant_domiciliu_judet = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    solicitant_domiciliu_localitate = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    solicitant_domiciliu_strada = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    solicitant_domiciliu_numar = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    solicitant_domiciliu_bloc = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    solicitant_domiciliu_scara = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    solicitant_domiciliu_etaj = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    solicitant_domiciliu_apartament = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    prefilled_fields = table.Column<string>(type: "jsonb", nullable: true),
                    office_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    consulto_office_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_owner = table.Column<bool>(type: "boolean", nullable: true),
                    office_address_judet = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    office_address_localitate = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    office_address_strada = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    office_address_numar = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    office_address_bloc = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    office_address_scara = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    office_address_etaj = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    office_address_apartament = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    acknowledged_ownership_docs = table.Column<bool>(type: "boolean", nullable: false),
                    acknowledged_submit_later = table.Column<bool>(type: "boolean", nullable: false),
                    acknowledged_owner_consent = table.Column<bool>(type: "boolean", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    admin_note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_formation_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_formation_requests_consulto_offices_consulto_office",
                        column: x => x.consulto_office_id,
                        principalSchema: "public",
                        principalTable: "consulto_offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_company_formation_requests_pfa_registrations_pfa_registrati",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "legal_consent_steps",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_consent_flow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    subtitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    checkbox_label = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_consent_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_legal_consent_steps_legal_consent_flows_legal_consent_flow_",
                        column: x => x.legal_consent_flow_id,
                        principalSchema: "public",
                        principalTable: "legal_consent_flows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_formation_consents",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_formation_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text_snapshot = table.Column<string>(type: "text", nullable: false),
                    checkbox_label_snapshot = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    accepted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_formation_consents", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_formation_consents_company_formation_requests_compa",
                        column: x => x.company_formation_request_id,
                        principalSchema: "public",
                        principalTable: "company_formation_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_formation_owners",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_formation_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    persoana_nume = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    persoana_prenume = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    persoana_cnp_encrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    persoana_cnp_masked = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    persoana_tip_act = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    persoana_serie_act = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    persoana_numar_act = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    persoana_autoritate_emitenta = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    persoana_data_emiterii = table.Column<DateOnly>(type: "date", nullable: true),
                    persoana_data_expirarii = table.Column<DateOnly>(type: "date", nullable: true),
                    persoana_domiciliu_judet = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    persoana_domiciliu_localitate = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    persoana_domiciliu_strada = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    persoana_domiciliu_numar = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    persoana_domiciliu_bloc = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    persoana_domiciliu_scara = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    persoana_domiciliu_etaj = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    persoana_domiciliu_apartament = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_formation_owners", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_formation_owners_company_formation_requests_company",
                        column: x => x.company_formation_request_id,
                        principalSchema: "public",
                        principalTable: "company_formation_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_formation_signatures",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_formation_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vector_data = table.Column<string>(type: "jsonb", nullable: true),
                    canvas_width = table.Column<int>(type: "integer", nullable: false),
                    canvas_height = table.Column<int>(type: "integer", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    device_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    os = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    browser = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    signed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_formation_signatures", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_formation_signatures_company_formation_requests_com",
                        column: x => x.company_formation_request_id,
                        principalSchema: "public",
                        principalTable: "company_formation_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_formation_consents_company_formation_request_id",
                schema: "public",
                table: "company_formation_consents",
                column: "company_formation_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_formation_owners_company_formation_request_id",
                schema: "public",
                table: "company_formation_owners",
                column: "company_formation_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_formation_requests_consulto_office_id",
                schema: "public",
                table: "company_formation_requests",
                column: "consulto_office_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_formation_requests_pfa_registration_id",
                schema: "public",
                table: "company_formation_requests",
                column: "pfa_registration_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_formation_signatures_company_formation_request_id",
                schema: "public",
                table: "company_formation_signatures",
                column: "company_formation_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_formation_signatures_idempotency_key",
                schema: "public",
                table: "company_formation_signatures",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_legal_consent_flows_context_version",
                schema: "public",
                table: "legal_consent_flows",
                columns: new[] { "context", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legal_consent_steps_legal_consent_flow_id",
                schema: "public",
                table: "legal_consent_steps",
                column: "legal_consent_flow_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_formation_signatures",
                schema: "public");

            migrationBuilder.DropTable(
                name: "company_formation_consents",
                schema: "public");

            migrationBuilder.DropTable(
                name: "company_formation_owners",
                schema: "public");

            migrationBuilder.DropTable(
                name: "company_formation_requests",
                schema: "public");

            migrationBuilder.DropTable(
                name: "legal_consent_steps",
                schema: "public");

            migrationBuilder.DropTable(
                name: "legal_consent_flows",
                schema: "public");

            migrationBuilder.DropTable(
                name: "consulto_offices",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "encrypted_value",
                schema: "public",
                table: "extracted_fields");
        }
    }
}
