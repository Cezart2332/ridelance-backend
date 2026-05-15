using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class CascadeDeleteUpdate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_chat_messages_users_sender_id",
            schema: "public",
            table: "chat_messages");

        migrationBuilder.DropForeignKey(
            name: "fk_chat_rooms_users_client_user_id",
            schema: "public",
            table: "chat_rooms");

        migrationBuilder.DropForeignKey(
            name: "fk_chat_rooms_users_professional_user_id",
            schema: "public",
            table: "chat_rooms");

        migrationBuilder.DropForeignKey(
            name: "fk_documents_pfa_registrations_pfa_registration_id",
            schema: "public",
            table: "documents");

        migrationBuilder.AddForeignKey(
            name: "fk_chat_messages_users_sender_id",
            schema: "public",
            table: "chat_messages",
            column: "sender_id",
            principalSchema: "public",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_chat_rooms_users_client_user_id",
            schema: "public",
            table: "chat_rooms",
            column: "client_user_id",
            principalSchema: "public",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_chat_rooms_users_professional_user_id",
            schema: "public",
            table: "chat_rooms",
            column: "professional_user_id",
            principalSchema: "public",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_documents_pfa_registrations_pfa_registration_id",
            schema: "public",
            table: "documents",
            column: "pfa_registration_id",
            principalSchema: "public",
            principalTable: "pfa_registrations",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_chat_messages_users_sender_id",
            schema: "public",
            table: "chat_messages");

        migrationBuilder.DropForeignKey(
            name: "fk_chat_rooms_users_client_user_id",
            schema: "public",
            table: "chat_rooms");

        migrationBuilder.DropForeignKey(
            name: "fk_chat_rooms_users_professional_user_id",
            schema: "public",
            table: "chat_rooms");

        migrationBuilder.DropForeignKey(
            name: "fk_documents_pfa_registrations_pfa_registration_id",
            schema: "public",
            table: "documents");

        migrationBuilder.AddForeignKey(
            name: "fk_chat_messages_users_sender_id",
            schema: "public",
            table: "chat_messages",
            column: "sender_id",
            principalSchema: "public",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_chat_rooms_users_client_user_id",
            schema: "public",
            table: "chat_rooms",
            column: "client_user_id",
            principalSchema: "public",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_chat_rooms_users_professional_user_id",
            schema: "public",
            table: "chat_rooms",
            column: "professional_user_id",
            principalSchema: "public",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_documents_pfa_registrations_pfa_registration_id",
            schema: "public",
            table: "documents",
            column: "pfa_registration_id",
            principalSchema: "public",
            principalTable: "pfa_registrations",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);
    }
}
