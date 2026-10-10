using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Emailul operațional per client (Migadu): mailbox-ul, identitatea RIDElance și jurnalul lor.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class ClientMailboxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_mailboxes",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    local_part = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    mailbox_password_encrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    mailbox_created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ops_identity_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ops_identity_local_part = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ops_identity_password_encrypted = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ops_identity_created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    handover_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    activated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    transferred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_mailboxes", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_mailboxes_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_mailboxes_user_id",
                schema: "public",
                table: "client_mailboxes",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_mailboxes_local_part",
                schema: "public",
                table: "client_mailboxes",
                column: "local_part",
                unique: true,
                filter: "local_part IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_client_mailboxes_pfa_registration_id",
                schema: "public",
                table: "client_mailboxes",
                column: "pfa_registration_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_mailboxes_status",
                schema: "public",
                table: "client_mailboxes",
                column: "status");

            migrationBuilder.CreateTable(
                name: "client_mailbox_audit_logs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_mailbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    performed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    details = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_mailbox_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_mailbox_audit_logs_client_mailboxes_client_mailbox_id",
                        column: x => x.client_mailbox_id,
                        principalSchema: "public",
                        principalTable: "client_mailboxes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_client_mailbox_audit_logs_client_mailbox_id",
                schema: "public",
                table: "client_mailbox_audit_logs",
                column: "client_mailbox_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "client_mailbox_audit_logs", schema: "public");
            migrationBuilder.DropTable(name: "client_mailboxes", schema: "public");
        }
    }
}
