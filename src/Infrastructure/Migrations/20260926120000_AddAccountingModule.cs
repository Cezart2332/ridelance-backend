using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861, CA1814 // Cod de migrație: tablouri constante și seed pe rânduri, ca în `Initial`.

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Modulul de contabilitate PFA (spec contabilitate B0): documentele Uber/Bolt și extracțiile,
    /// regulile fiscale versionate, declarațiile cu versiuni și linii, setările contabile append-only,
    /// casa de marcat, ledger-ul, registrele, perioadele, auditul și joburile.
    ///
    /// Toate cheile străine sunt RESTRICT: datele fiscale nu se șterg în cascadă. Concurența pe
    /// `declaration_versions` și `ledger_entries` folosește coloana de sistem `xmin`, fără coloană nouă.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect: operațiile de tabel au fost generate din
    /// configurări într-un context separat (restul modelului exclus), apoi curățate; seed-ul e la final.
    /// </summary>
    public partial class AddAccountingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.CreateTable(
                name: "anaf_declaration_schemas",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    declaration_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    xsd_path = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    validator_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anaf_declaration_schemas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_logs_pfa_registration_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audit_logs_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "background_jobs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    parameters_json = table.Column<string>(type: "jsonb", nullable: false),
                    progress_done = table.Column<int>(type: "integer", nullable: false),
                    progress_total = table.Column<int>(type: "integer", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    file_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_background_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_background_jobs_document_file_document_id",
                        column: x => x.file_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_background_jobs_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_register_states",
                schema: "public",
                columns: table => new
                {
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_requested = table.Column<bool>(type: "boolean", nullable: false),
                    cash_requested_answered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cash_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    activation_date = table.Column<DateOnly>(type: "date", nullable: true),
                    verified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evidence_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_register_states", x => x.pfa_registration_id);
                    table.ForeignKey(
                        name: "fk_cash_register_states_document_evidence_document_id",
                        column: x => x.evidence_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_register_states_pfa_registration_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_register_states_user_verified_by_user_id",
                        column: x => x.verified_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "d100_rules",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    pending_confirmation = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    parameters_json = table.Column<string>(type: "jsonb", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_d100_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "declarations",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_declarations", x => x.id);
                    table.ForeignKey(
                        name: "fk_declarations_pfa_registration_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_category_rules",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    vehicle_related = table.Column<bool>(type: "boolean", nullable: false),
                    default_deductibility = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    counterparty_pattern = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_category_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pfa_accounting_engagements",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_accounting_engagements", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_accounting_engagements_pfa_registration_pfa_registratio",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pfa_accounting_periods",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_accounting_periods", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_accounting_periods_pfa_registration_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pfa_accounting_periods_user_closed_by_user_id",
                        column: x => x.closed_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pfa_accounting_settings",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_accounting_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_accounting_settings_pfa_registration_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pfa_accounting_settings_user_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pfa_assets",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    acquisition_date = table.Column<DateOnly>(type: "date", nullable: false),
                    acquisition_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    disposed_date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_assets_document_document_id",
                        column: x => x.document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pfa_assets_pfa_registration_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_documents",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    platform = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    document_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    extraction_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_documents_document_source_document_id",
                        column: x => x.source_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_documents_pfa_registration_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_documents_user_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_documents_user_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "retention_policies",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    years_after = table.Column<int>(type: "integer", nullable: false),
                    start_month = table.Column<int>(type: "integer", nullable: false),
                    start_day = table.Column<int>(type: "integer", nullable: false),
                    confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retention_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_tax_profiles",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    vat_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    income_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    treaty = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    d100rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    d100rate_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    residence_cert_valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    residence_cert_valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    residence_cert_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_tax_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_supplier_tax_profiles_document_residence_cert_document_id",
                        column: x => x.residence_cert_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vat_rates",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vat_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "declaration_versions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    declaration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    schema_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    xml_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pdf_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    validation_result_json = table.Column<string>(type: "jsonb", nullable: true),
                    receipt_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    rectification_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status_history_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_declaration_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_declaration_versions_anaf_declaration_schemas_schema_id",
                        column: x => x.schema_id,
                        principalSchema: "public",
                        principalTable: "anaf_declaration_schemas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_declaration_versions_declarations_declaration_id",
                        column: x => x.declaration_id,
                        principalSchema: "public",
                        principalTable: "declarations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_declaration_versions_document_pdf_document_id",
                        column: x => x.pdf_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_declaration_versions_document_receipt_document_id",
                        column: x => x.receipt_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_declaration_versions_document_xml_document_id",
                        column: x => x.xml_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_declaration_versions_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_extractions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    supplier_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    supplier_vat_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    period_from = table.Column<DateOnly>(type: "date", nullable: true),
                    period_to = table.Column<DateOnly>(type: "date", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    commission_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    other_amounts_json = table.Column<string>(type: "jsonb", nullable: false),
                    source_snippets_json = table.Column<string>(type: "jsonb", nullable: false),
                    checks_result_json = table.Column<string>(type: "jsonb", nullable: false),
                    model_confidence = table.Column<double>(type: "double precision", nullable: true),
                    model_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_manual_edit = table.Column<bool>(type: "boolean", nullable: false),
                    manually_edited_fields_json = table.Column<string>(type: "jsonb", nullable: false),
                    edit_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_extractions", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_extractions_platform_document_platform_document_id",
                        column: x => x.platform_document_id,
                        principalSchema: "public",
                        principalTable: "platform_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_extractions_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    document_label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    platform_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bank_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    counterparty = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    transaction_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    vehicle_related = table.Column<bool>(type: "boolean", nullable: false),
                    deductibility_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    deductible_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    deductible_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    deductibility_setting_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deductibility_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deductibility_valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    accounting_period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    closed_period_flag = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_ledger_entries_bank_transaction_bank_transaction_id",
                        column: x => x.bank_transaction_id,
                        principalSchema: "public",
                        principalTable: "bank_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_document_source_document_id",
                        column: x => x.source_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_expense_category_rules_deductibility_rule_id",
                        column: x => x.deductibility_rule_id,
                        principalSchema: "public",
                        principalTable: "expense_category_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_pfa_accounting_setting_deductibility_setting",
                        column: x => x.deductibility_setting_id,
                        principalSchema: "public",
                        principalTable: "pfa_accounting_settings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_pfa_registration_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_platform_document_platform_document_id",
                        column: x => x.platform_document_id,
                        principalSchema: "public",
                        principalTable: "platform_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "declaration_lines",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    declaration_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    @base = table.Column<decimal>(name: "base", type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    explanation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    supplier_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    supplier_vat_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    operation_type = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    treaty = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    residence_cert_valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    residence_cert_valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_declaration_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_declaration_lines_declaration_version_declaration_version_id",
                        column: x => x.declaration_version_id,
                        principalSchema: "public",
                        principalTable: "declaration_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_declaration_lines_platform_document_source_document_id",
                        column: x => x.source_document_id,
                        principalSchema: "public",
                        principalTable: "platform_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_documents",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    merchant_cui = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: true),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    items_json = table.Column<string>(type: "jsonb", nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_documents_document_document_id",
                        column: x => x.document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_documents_ledger_entry_ledger_entry_id",
                        column: x => x.ledger_entry_id,
                        principalSchema: "public",
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_documents_pfa_registration_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_documents_user_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "period_corrections",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    change_json = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_period_corrections", x => x.id);
                    table.ForeignKey(
                        name: "fk_period_corrections_ledger_entries_ledger_entry_id",
                        column: x => x.ledger_entry_id,
                        principalSchema: "public",
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_period_corrections_pfa_registration_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_period_corrections_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "z_reports",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    z_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_z_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_z_reports_document_document_id",
                        column: x => x.document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_z_reports_ledger_entries_ledger_entry_id",
                        column: x => x.ledger_entry_id,
                        principalSchema: "public",
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_z_reports_pfa_registration_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anaf_declaration_schemas_declaration_type_valid_from",
                schema: "public",
                table: "anaf_declaration_schemas",
                columns: new[] { "declaration_type", "valid_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_entity_id",
                schema: "public",
                table: "audit_logs",
                columns: new[] { "entity", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_pfa_registration_id_at_utc",
                schema: "public",
                table: "audit_logs",
                columns: new[] { "pfa_registration_id", "at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id",
                schema: "public",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_background_jobs_created_by_user_id",
                schema: "public",
                table: "background_jobs",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_background_jobs_file_document_id",
                schema: "public",
                table: "background_jobs",
                column: "file_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_background_jobs_status_created_at_utc",
                schema: "public",
                table: "background_jobs",
                columns: new[] { "status", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_register_states_evidence_document_id",
                schema: "public",
                table: "cash_register_states",
                column: "evidence_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_register_states_verified_by_user_id",
                schema: "public",
                table: "cash_register_states",
                column: "verified_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_d100_rules_code_valid_from",
                schema: "public",
                table: "d100_rules",
                columns: new[] { "code", "valid_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_declaration_lines_declaration_version_id",
                schema: "public",
                table: "declaration_lines",
                column: "declaration_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_declaration_lines_source_document_id",
                schema: "public",
                table: "declaration_lines",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_declaration_versions_created_by_user_id",
                schema: "public",
                table: "declaration_versions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_declaration_versions_declaration_id_version_no",
                schema: "public",
                table: "declaration_versions",
                columns: new[] { "declaration_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_declaration_versions_pdf_document_id",
                schema: "public",
                table: "declaration_versions",
                column: "pdf_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_declaration_versions_receipt_document_id",
                schema: "public",
                table: "declaration_versions",
                column: "receipt_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_declaration_versions_schema_id",
                schema: "public",
                table: "declaration_versions",
                column: "schema_id");

            migrationBuilder.CreateIndex(
                name: "ix_declaration_versions_status",
                schema: "public",
                table: "declaration_versions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_declaration_versions_xml_document_id",
                schema: "public",
                table: "declaration_versions",
                column: "xml_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_declarations_pfa_registration_id_period_type",
                schema: "public",
                table: "declarations",
                columns: new[] { "pfa_registration_id", "period", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_extractions_created_by_user_id",
                schema: "public",
                table: "document_extractions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_extractions_platform_document_id",
                schema: "public",
                table: "document_extractions",
                column: "platform_document_id",
                unique: true,
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "ix_document_extractions_platform_document_id_version",
                schema: "public",
                table: "document_extractions",
                columns: new[] { "platform_document_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_extractions_supplier_vat_id_invoice_number",
                schema: "public",
                table: "document_extractions",
                columns: new[] { "supplier_vat_id", "invoice_number" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_currency_date_source",
                schema: "public",
                table: "exchange_rates",
                columns: new[] { "currency", "date", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_category_rules_category_valid_from",
                schema: "public",
                table: "expense_category_rules",
                columns: new[] { "category", "valid_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_documents_document_id",
                schema: "public",
                table: "expense_documents",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_documents_ledger_entry_id",
                schema: "public",
                table: "expense_documents",
                column: "ledger_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_documents_pfa_registration_id",
                schema: "public",
                table: "expense_documents",
                column: "pfa_registration_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_documents_uploaded_by_user_id",
                schema: "public",
                table: "expense_documents",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_bank_transaction_id",
                schema: "public",
                table: "ledger_entries",
                column: "bank_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_created_by_user_id",
                schema: "public",
                table: "ledger_entries",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_deductibility_rule_id",
                schema: "public",
                table: "ledger_entries",
                column: "deductibility_rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_deductibility_setting_id",
                schema: "public",
                table: "ledger_entries",
                column: "deductibility_setting_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_pfa_registration_id_accounting_period",
                schema: "public",
                table: "ledger_entries",
                columns: new[] { "pfa_registration_id", "accounting_period" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_pfa_registration_id_date",
                schema: "public",
                table: "ledger_entries",
                columns: new[] { "pfa_registration_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_platform_document_id",
                schema: "public",
                table: "ledger_entries",
                column: "platform_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_source_document_id",
                schema: "public",
                table: "ledger_entries",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_source_external_id",
                schema: "public",
                table: "ledger_entries",
                columns: new[] { "source", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_period_corrections_created_by_user_id",
                schema: "public",
                table: "period_corrections",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_period_corrections_ledger_entry_id",
                schema: "public",
                table: "period_corrections",
                column: "ledger_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_period_corrections_pfa_registration_id_period",
                schema: "public",
                table: "period_corrections",
                columns: new[] { "pfa_registration_id", "period" });

            migrationBuilder.CreateIndex(
                name: "ix_pfa_accounting_engagements_pfa_registration_id_start_date",
                schema: "public",
                table: "pfa_accounting_engagements",
                columns: new[] { "pfa_registration_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_pfa_accounting_periods_closed_by_user_id",
                schema: "public",
                table: "pfa_accounting_periods",
                column: "closed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pfa_accounting_periods_pfa_registration_id_period",
                schema: "public",
                table: "pfa_accounting_periods",
                columns: new[] { "pfa_registration_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pfa_accounting_settings_changed_by_user_id",
                schema: "public",
                table: "pfa_accounting_settings",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pfa_accounting_settings_pfa_registration_id_key_valid_from",
                schema: "public",
                table: "pfa_accounting_settings",
                columns: new[] { "pfa_registration_id", "key", "valid_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pfa_assets_document_id",
                schema: "public",
                table: "pfa_assets",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_pfa_assets_pfa_registration_id",
                schema: "public",
                table: "pfa_assets",
                column: "pfa_registration_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_documents_pfa_registration_id_file_hash",
                schema: "public",
                table: "platform_documents",
                columns: new[] { "pfa_registration_id", "file_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_documents_pfa_registration_id_period",
                schema: "public",
                table: "platform_documents",
                columns: new[] { "pfa_registration_id", "period" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_documents_reviewed_by_user_id",
                schema: "public",
                table: "platform_documents",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_documents_source_document_id",
                schema: "public",
                table: "platform_documents",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_documents_uploaded_by_user_id",
                schema: "public",
                table: "platform_documents",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_retention_policies_valid_from",
                schema: "public",
                table: "retention_policies",
                column: "valid_from",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_tax_profiles_residence_cert_document_id",
                schema: "public",
                table: "supplier_tax_profiles",
                column: "residence_cert_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_tax_profiles_vat_id_valid_from",
                schema: "public",
                table: "supplier_tax_profiles",
                columns: new[] { "vat_id", "valid_from" });

            migrationBuilder.CreateIndex(
                name: "ix_vat_rates_valid_from",
                schema: "public",
                table: "vat_rates",
                column: "valid_from",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_z_reports_document_id",
                schema: "public",
                table: "z_reports",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_z_reports_ledger_entry_id",
                schema: "public",
                table: "z_reports",
                column: "ledger_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_z_reports_pfa_registration_id_z_number",
                schema: "public",
                table: "z_reports",
                columns: new[] { "pfa_registration_id", "z_number" },
                unique: true);

            SeedAccountingRules(migrationBuilder);
        }

        /// <summary>
        /// Seed-ul cerut de B0. Valorile marcate DE CONFIRMAT în spec rămân neconfirmate: cota D100
        /// pentru Uber e necompletată și neconfirmată (blochează D100 până la confirmare), iar
        /// `D100_RENT_INDIVIDUAL` e dezactivată. Codurile TVA ale platformelor se verifică pe o
        /// factură reală; certificatele de rezidență se încarcă din „Reguli fiscale”.
        /// </summary>
        private static void SeedAccountingRules(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "public",
                table: "vat_rates",
                // Tipurile explicite: migrațiile nu au model în Designer, deci EF nu le poate deduce.
                columnTypes: ["uuid", "numeric(9,4)", "date", "date"],
                columns: ["id", "rate", "valid_from", "valid_to"],
                values: new object[,]
                {
                    { new Guid("5d0c7b1e-0a5e-4c43-9a1b-000000000019"), 19m, new DateOnly(2017, 1, 1), new DateOnly(2025, 7, 31) },
                    { new Guid("5d0c7b1e-0a5e-4c43-9a1b-000000000021"), 21m, new DateOnly(2025, 8, 1), null },
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "supplier_tax_profiles",
                // Tipurile explicite: migrațiile nu au model în Designer, deci EF nu le poate deduce.
                columnTypes: ["uuid", "character varying(256)", "character varying(2)", "character varying(32)", "character varying(32)", "character varying(128)", "numeric(9,4)", "boolean", "date", "date", "date", "date", "uuid", "character varying(500)"],
                columns:
                [
                    "id", "supplier_name", "country", "vat_id", "income_type", "treaty", "d100rate",
                    "d100rate_confirmed", "valid_from", "valid_to", "residence_cert_valid_from",
                    "residence_cert_valid_to", "residence_cert_document_id", "note",
                ],
                values: new object[,]
                {
                    {
                        new Guid("7a3f1c2d-4b5e-4f60-8a71-0000000b0170"), "Bolt Operations OÜ", "EE", "EE102090374", "COMMISSION",
                        "Convenția RO–EE", 2m, true, new DateOnly(2025, 1, 1), null, null, null, null,
                        "Cod TVA de verificat pe o factură reală. Certificatul de rezidență se încarcă din „Reguli fiscale”.",
                    },
                    {
                        new Guid("7a3f1c2d-4b5e-4f60-8a71-0000000b0e70"), "Uber B.V.", "NL", "NL852071588B01", "COMMISSION",
                        "Convenția RO–NL", null, false, new DateOnly(2025, 1, 1), null, null, null, null,
                        "Cota D100 DE CONFIRMAT cu contabilul (spec §6 pct. 1). Cod TVA de verificat pe o factură reală.",
                    },
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "d100_rules",
                // Tipurile explicite: migrațiile nu au model în Designer, deci EF nu le poate deduce.
                columnTypes: ["uuid", "character varying(48)", "boolean", "boolean", "character varying(500)", "jsonb", "date", "date"],
                columns: ["id", "code", "enabled", "pending_confirmation", "description", "parameters_json", "valid_from", "valid_to"],
                values: new object[,]
                {
                    {
                        new Guid("3e8b2f4a-1c6d-4e7f-9a0b-0000000d1001"), "D100CommissionNonresident", true, false,
                        "Impozit pe veniturile nerezidenților din comisioanele reținute de platforme.",
                        "{\"base\":\"COMMISSION_AMOUNT_RON\",\"rateSource\":\"SupplierTaxProfile.D100Rate\"}",
                        new DateOnly(2025, 1, 1), null,
                    },
                    {
                        new Guid("3e8b2f4a-1c6d-4e7f-9a0b-0000000d1002"), "D100RentIndividual", false, true,
                        "Impozit pe chiria plătită persoanelor fizice. DE CONFIRMAT: bază, cotă, sursa datelor.",
                        "{}", new DateOnly(2025, 1, 1), null,
                    },
                });

            // Termenul de păstrare din documentul clientului: 5 ani de la 1 iulie a anului următor.
            // DE CONFIRMAT cu contabilul (spec §6 pct. 12).
            migrationBuilder.InsertData(
                schema: "public",
                table: "retention_policies",
                // Tipurile explicite: migrațiile nu au model în Designer, deci EF nu le poate deduce.
                columnTypes: ["uuid", "integer", "integer", "integer", "boolean", "date", "date"],
                columns: ["id", "years_after", "start_month", "start_day", "confirmed", "valid_from", "valid_to"],
                values: [new Guid("9c1d2e3f-4a5b-4c6d-8e7f-000000000075"), 5, 7, 1, false, new DateOnly(2000, 1, 1), null]);

            // Categoriile de cheltuieli ale clasificării deterministe (B6), pentru ridesharing.
            migrationBuilder.InsertData(
                schema: "public",
                table: "expense_category_rules",
                // Tipurile explicite: migrațiile nu au model în Designer, deci EF nu le poate deduce.
                columnTypes: ["uuid", "character varying(64)", "character varying(128)", "boolean", "character varying(48)", "character varying(256)", "date", "date"],
                columns: ["id", "category", "label", "vehicle_related", "default_deductibility", "counterparty_pattern", "valid_from", "valid_to"],
                values: new object[,]
                {
                    { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca701"), "FUEL", "Combustibil", true, "Percent100", "OMV|PETROM|ROMPETROL|MOL|LUKOIL|SOCAR", new DateOnly(2025, 1, 1), null },
                    { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca702"), "CAR_SERVICE", "Service auto", true, "Percent100", "SERVICE|AUTO", new DateOnly(2025, 1, 1), null },
                    { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca703"), "CAR_INSURANCE", "Asigurare auto", true, "Percent100", "ALLIANZ|GROUPAMA|OMNIASIG|GENERALI", new DateOnly(2025, 1, 1), null },
                    { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca704"), "CAR_WASH", "Spălătorie auto", true, "Percent100", "WASH|SPALATORIE", new DateOnly(2025, 1, 1), null },
                    { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca705"), "PHONE", "Telefonie mobilă", false, "Percent100", "ORANGE|VODAFONE|DIGI", new DateOnly(2025, 1, 1), null },
                    { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca706"), "CASH_REGISTER", "Casă de marcat și consumabile", false, "Percent100", "DATECS|TREMOL", new DateOnly(2025, 1, 1), null },
                    { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca707"), "DEPRECIATION", "Amortizare", true, "SpecialRule", null, new DateOnly(2025, 1, 1), null },
                    { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000ca708"), "PERSONAL", "Cheltuieli personale", false, "NonDeductible", null, new DateOnly(2025, 1, 1), null },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "background_jobs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "cash_register_states",
                schema: "public");

            migrationBuilder.DropTable(
                name: "d100_rules",
                schema: "public");

            migrationBuilder.DropTable(
                name: "declaration_lines",
                schema: "public");

            migrationBuilder.DropTable(
                name: "document_extractions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "exchange_rates",
                schema: "public");

            migrationBuilder.DropTable(
                name: "expense_documents",
                schema: "public");

            migrationBuilder.DropTable(
                name: "period_corrections",
                schema: "public");

            migrationBuilder.DropTable(
                name: "pfa_accounting_engagements",
                schema: "public");

            migrationBuilder.DropTable(
                name: "pfa_accounting_periods",
                schema: "public");

            migrationBuilder.DropTable(
                name: "pfa_assets",
                schema: "public");

            migrationBuilder.DropTable(
                name: "retention_policies",
                schema: "public");

            migrationBuilder.DropTable(
                name: "supplier_tax_profiles",
                schema: "public");

            migrationBuilder.DropTable(
                name: "vat_rates",
                schema: "public");

            migrationBuilder.DropTable(
                name: "z_reports",
                schema: "public");

            migrationBuilder.DropTable(
                name: "declaration_versions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "public");

            migrationBuilder.DropTable(
                name: "anaf_declaration_schemas",
                schema: "public");

            migrationBuilder.DropTable(
                name: "declarations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "expense_category_rules",
                schema: "public");

            migrationBuilder.DropTable(
                name: "pfa_accounting_settings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "platform_documents",
                schema: "public");
        }
    }
}
