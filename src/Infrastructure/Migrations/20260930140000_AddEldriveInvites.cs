using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Invitațiile Eldrive ale clienților. ID-ul invitației la Eldrive se păstrează pentru ștergere,
/// iar rândurile șterse rămân ca evidență; indexul unic acoperă doar invitațiile active.
/// </summary>
public partial class AddEldriveInvites : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "eldrive_invites",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                eldrive_invite_id = table.Column<long>(type: "bigint", nullable: false),
                eldrive_user_id = table.Column<long>(type: "bigint", nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                removed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                removed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_eldrive_invites", x => x.id);
                table.ForeignKey(
                    name: "fk_eldrive_invites_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_eldrive_invites_user_id",
            schema: "public",
            table: "eldrive_invites",
            column: "user_id",
            unique: true,
            filter: "removed_at_utc IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "eldrive_invites",
            schema: "public");
    }
}
