using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Conexiunea ANAF (OAuth cu certificatul împuternicitului), clienții conectați la e-Factura și
    /// mesajele e-Factura descărcate. Tokenurile se păstrează criptate.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddAnafEFactura : Migration
    {
        private const string Schema = "public";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anaf_connections",
                schema: Schema,
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    access_token_protected = table.Column<string>(type: "text", nullable: false),
                    refresh_token_protected = table.Column<string>(type: "text", nullable: false),
                    access_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    refresh_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    connected_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    refreshed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    disconnected_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anaf_connections", x => x.id);
                    table.ForeignKey("fk_anaf_connections_users_user_id", x => x.user_id, principalSchema: Schema, principalTable: "users", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(name: "ix_anaf_connections_status", schema: Schema, table: "anaf_connections", column: "status");
            migrationBuilder.CreateIndex(name: "ix_anaf_connections_user_id", schema: Schema, table: "anaf_connections", column: "user_id");

            migrationBuilder.CreateTable(
                name: "anaf_authorization_requests",
                schema: Schema,
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anaf_authorization_requests", x => x.id);
                    table.ForeignKey("fk_anaf_authorization_requests_users_user_id", x => x.user_id, principalSchema: Schema, principalTable: "users", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(name: "ix_anaf_authorization_requests_state", schema: Schema, table: "anaf_authorization_requests", column: "state", unique: true);
            migrationBuilder.CreateIndex(name: "ix_anaf_authorization_requests_user_id", schema: Schema, table: "anaf_authorization_requests", column: "user_id");

            migrationBuilder.CreateTable(
                name: "anaf_pfa_links",
                schema: Schema,
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    enabled_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_sync_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anaf_pfa_links", x => x.id);
                    table.ForeignKey("fk_anaf_pfa_links_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, principalSchema: Schema, principalTable: "pfa_registrations", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("fk_anaf_pfa_links_users_enabled_by_user_id", x => x.enabled_by_user_id, principalSchema: Schema, principalTable: "users", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(name: "ix_anaf_pfa_links_pfa_registration_id", schema: Schema, table: "anaf_pfa_links", column: "pfa_registration_id", unique: true);
            migrationBuilder.CreateIndex(name: "ix_anaf_pfa_links_enabled_by_user_id", schema: Schema, table: "anaf_pfa_links", column: "enabled_by_user_id");

            migrationBuilder.CreateTable(
                name: "efactura_messages",
                schema: Schema,
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    anaf_message_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    anaf_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    anaf_created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    upload_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    zip_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pdf_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    download_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    downloaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    supplier_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    supplier_cif = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    customer_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    customer_cif = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    is_credit_note = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_efactura_messages", x => x.id);
                    table.ForeignKey("fk_efactura_messages_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, principalSchema: Schema, principalTable: "pfa_registrations", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("fk_efactura_messages_document_zip_document_id", x => x.zip_document_id, principalSchema: Schema, principalTable: "documents", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("fk_efactura_messages_document_pdf_document_id", x => x.pdf_document_id, principalSchema: Schema, principalTable: "documents", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(name: "ix_efactura_messages_pfa_registration_id_anaf_message_id", schema: Schema, table: "efactura_messages", columns: ["pfa_registration_id", "anaf_message_id"], unique: true);
            migrationBuilder.CreateIndex(name: "ix_efactura_messages_zip_document_id", schema: Schema, table: "efactura_messages", column: "zip_document_id");
            migrationBuilder.CreateIndex(name: "ix_efactura_messages_pdf_document_id", schema: Schema, table: "efactura_messages", column: "pdf_document_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "efactura_messages", schema: Schema);
            migrationBuilder.DropTable(name: "anaf_pfa_links", schema: Schema);
            migrationBuilder.DropTable(name: "anaf_authorization_requests", schema: Schema);
            migrationBuilder.DropTable(name: "anaf_connections", schema: Schema);
        }
    }
}
