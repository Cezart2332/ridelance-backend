using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable IDE0161, CA1861

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "users",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    first_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    last_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    refresh_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    refresh_token_expiry_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_rooms",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_rooms", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_rooms_users_client_user_id",
                        column: x => x.client_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chat_rooms_users_professional_user_id",
                        column: x => x.professional_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pfa_registrations",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    contract_duration = table.Column<int>(type: "integer", nullable: true),
                    street = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    city = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    county = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_owner = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pfa_registrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_pfa_registrations_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    auth = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_push_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_messages_chat_rooms_chat_room_id",
                        column: x => x.chat_room_id,
                        principalSchema: "public",
                        principalTable: "chat_rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_messages_users_sender_id",
                        column: x => x.sender_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    stored_file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    encrypted_file_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    encryption_iv = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_documents_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_documents_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_chat_room_id",
                schema: "public",
                table: "chat_messages",
                column: "chat_room_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_sender_id",
                schema: "public",
                table: "chat_messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_sent_at_utc",
                schema: "public",
                table: "chat_messages",
                column: "sent_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_chat_rooms_client_user_id_professional_user_id",
                schema: "public",
                table: "chat_rooms",
                columns: new[] { "client_user_id", "professional_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_rooms_professional_user_id",
                schema: "public",
                table: "chat_rooms",
                column: "professional_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_pfa_registration_id",
                schema: "public",
                table: "documents",
                column: "pfa_registration_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_user_id",
                schema: "public",
                table: "documents",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id",
                schema: "public",
                table: "notifications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pfa_registrations_user_id",
                schema: "public",
                table: "pfa_registrations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_endpoint",
                schema: "public",
                table: "push_subscriptions",
                column: "endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_user_id",
                schema: "public",
                table: "push_subscriptions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "public",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_messages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "public");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "public");

            migrationBuilder.DropTable(
                name: "push_subscriptions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "chat_rooms",
                schema: "public");

            migrationBuilder.DropTable(
                name: "pfa_registrations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "users",
                schema: "public");
        }
    }
}
