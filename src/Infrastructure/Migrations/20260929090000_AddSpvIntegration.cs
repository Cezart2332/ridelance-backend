using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// SPV prin aplicația desktop RIDElance SPV: cheile aplicației (doar hash-ul), trimiterile ei,
    /// mesajele SPV (unice după id-ul ANAF) și cererile către SPV.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddSpvIntegration : Migration
    {
        private const string Schema = "public";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "spv_agent_keys",
                schema: Schema,
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_used_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spv_agent_keys", x => x.id);
                    table.ForeignKey("fk_spv_agent_keys_users_user_id", x => x.user_id, principalSchema: Schema, principalTable: "users", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(name: "ix_spv_agent_keys_key_hash", schema: Schema, table: "spv_agent_keys", column: "key_hash", unique: true);
            migrationBuilder.CreateIndex(name: "ix_spv_agent_keys_user_id", schema: Schema, table: "spv_agent_keys", column: "user_id");

            migrationBuilder.CreateTable(
                name: "spv_sync_runs",
                schema: Schema,
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    machine = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    agent_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lease_until_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    listed = table.Column<int>(type: "integer", nullable: false),
                    received = table.Column<int>(type: "integer", nullable: false),
                    requests_sent = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spv_sync_runs", x => x.id);
                    table.ForeignKey("fk_spv_sync_runs_spv_agent_keys_agent_key_id", x => x.agent_key_id, principalSchema: Schema, principalTable: "spv_agent_keys", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(name: "ix_spv_sync_runs_status_started_at_utc", schema: Schema, table: "spv_sync_runs", columns: ["status", "started_at_utc"]);
            migrationBuilder.CreateIndex(name: "ix_spv_sync_runs_agent_key_id", schema: Schema, table: "spv_sync_runs", column: "agent_key_id");

            migrationBuilder.CreateTable(
                name: "spv_messages",
                schema: Schema,
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    anaf_message_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cif = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    anaf_created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    declaration_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    spv_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    read_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spv_messages", x => x.id);
                    table.ForeignKey("fk_spv_messages_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, principalSchema: Schema, principalTable: "pfa_registrations", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("fk_spv_messages_document_document_id", x => x.document_id, principalSchema: Schema, principalTable: "documents", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("fk_spv_messages_declaration_versions_declaration_version_id", x => x.declaration_version_id, principalSchema: Schema, principalTable: "declaration_versions", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(name: "ix_spv_messages_anaf_message_id", schema: Schema, table: "spv_messages", column: "anaf_message_id", unique: true);
            migrationBuilder.CreateIndex(name: "ix_spv_messages_pfa_registration_id_anaf_created_at_utc", schema: Schema, table: "spv_messages", columns: ["pfa_registration_id", "anaf_created_at_utc"]);
            migrationBuilder.CreateIndex(name: "ix_spv_messages_status", schema: Schema, table: "spv_messages", column: "status");
            migrationBuilder.CreateIndex(name: "ix_spv_messages_document_id", schema: Schema, table: "spv_messages", column: "document_id");
            migrationBuilder.CreateIndex(name: "ix_spv_messages_declaration_version_id", schema: Schema, table: "spv_messages", column: "declaration_version_id");

            migrationBuilder.CreateTable(
                name: "spv_requests",
                schema: Schema,
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cui = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parameters_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    claimed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    claimed_by_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    anaf_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    answer_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spv_requests", x => x.id);
                    table.ForeignKey("fk_spv_requests_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, principalSchema: Schema, principalTable: "pfa_registrations", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("fk_spv_requests_users_requested_by_user_id", x => x.requested_by_user_id, principalSchema: Schema, principalTable: "users", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(name: "ix_spv_requests_status", schema: Schema, table: "spv_requests", column: "status");
            migrationBuilder.CreateIndex(name: "ix_spv_requests_anaf_request_id", schema: Schema, table: "spv_requests", column: "anaf_request_id");
            migrationBuilder.CreateIndex(name: "ix_spv_requests_pfa_registration_id", schema: Schema, table: "spv_requests", column: "pfa_registration_id");
            migrationBuilder.CreateIndex(name: "ix_spv_requests_requested_by_user_id", schema: Schema, table: "spv_requests", column: "requested_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "spv_requests", schema: Schema);
            migrationBuilder.DropTable(name: "spv_messages", schema: Schema);
            migrationBuilder.DropTable(name: "spv_sync_runs", schema: Schema);
            migrationBuilder.DropTable(name: "spv_agent_keys", schema: Schema);
        }
    }
}
