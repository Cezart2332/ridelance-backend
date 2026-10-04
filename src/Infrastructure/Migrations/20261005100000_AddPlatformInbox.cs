using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Încărcarea globală din „Clienți PFA”: documentele încărcate fără client ales, cu alocarea lor automată
/// (CUI, numele fișierului, comision) sau motivul pentru care așteaptă Adminul.
/// </summary>
public partial class AddPlatformInbox : Migration
{
    private const string Schema = "public";
    private const string Table = "platform_inbox_items";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: Table,
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                file_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                matched_by = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                detected_cui = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                platform = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                document_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                commission_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                period_from = table.Column<DateOnly>(type: "date", nullable: true),
                period_to = table.Column<DateOnly>(type: "date", nullable: true),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: true),
                platform_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_platform_inbox_items", x => x.id);
                table.ForeignKey("fk_platform_inbox_items_documents_source_document_id", x => x.source_document_id, "documents", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_platform_inbox_items_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, "pfa_registrations", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_platform_inbox_items_platform_documents_platform_document_id", x => x.platform_document_id, "platform_documents", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_platform_inbox_items_users_uploaded_by_user_id", x => x.uploaded_by_user_id, "users", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_platform_inbox_items_users_resolved_by_user_id", x => x.resolved_by_user_id, "users", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_platform_inbox_items_status", schema: Schema, table: Table, column: "status");
        migrationBuilder.CreateIndex(name: "ix_platform_inbox_items_file_hash", schema: Schema, table: Table, column: "file_hash");
        migrationBuilder.CreateIndex(name: "ix_platform_inbox_items_source_document_id", schema: Schema, table: Table, column: "source_document_id");
        migrationBuilder.CreateIndex(name: "ix_platform_inbox_items_pfa_registration_id", schema: Schema, table: Table, column: "pfa_registration_id");
        migrationBuilder.CreateIndex(name: "ix_platform_inbox_items_platform_document_id", schema: Schema, table: Table, column: "platform_document_id");
        migrationBuilder.CreateIndex(name: "ix_platform_inbox_items_uploaded_by_user_id", schema: Schema, table: Table, column: "uploaded_by_user_id");
        migrationBuilder.CreateIndex(name: "ix_platform_inbox_items_resolved_by_user_id", schema: Schema, table: Table, column: "resolved_by_user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: Table, schema: Schema);
}
