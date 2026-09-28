using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Cererile D700 pentru codul de TVA art. 317, generate automat când clientul răspunde „Nu” la
    /// întrebarea despre TVA intracomunitar din onboarding. Concurența optimistă folosește coloana de
    /// sistem <c>xmin</c>, fără coloană nouă.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddVatRegistrationRequests : Migration
    {
        private const string Table = "vat_registration_requests";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: Table,
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    missing_data = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xml_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pdf_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    validation_json = table.Column<string>(type: "jsonb", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    vat_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    vat_code_valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    certificate_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status_history_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vat_registration_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_vat_registration_requests_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vat_registration_requests_document_xml_document_id",
                        column: x => x.xml_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vat_registration_requests_document_pdf_document_id",
                        column: x => x.pdf_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vat_registration_requests_document_certificate_document_id",
                        column: x => x.certificate_document_id,
                        principalSchema: "public",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vat_registration_requests_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vat_registration_requests_pfa_registration_id",
                schema: "public",
                table: Table,
                column: "pfa_registration_id");
            migrationBuilder.CreateIndex(
                name: "ix_vat_registration_requests_status",
                schema: "public",
                table: Table,
                column: "status");
            migrationBuilder.CreateIndex(
                name: "ix_vat_registration_requests_xml_document_id",
                schema: "public",
                table: Table,
                column: "xml_document_id");
            migrationBuilder.CreateIndex(
                name: "ix_vat_registration_requests_pdf_document_id",
                schema: "public",
                table: Table,
                column: "pdf_document_id");
            migrationBuilder.CreateIndex(
                name: "ix_vat_registration_requests_certificate_document_id",
                schema: "public",
                table: Table,
                column: "certificate_document_id");
            migrationBuilder.CreateIndex(
                name: "ix_vat_registration_requests_created_by_user_id",
                schema: "public",
                table: Table,
                column: "created_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: Table, schema: "public");
        }
    }
}
