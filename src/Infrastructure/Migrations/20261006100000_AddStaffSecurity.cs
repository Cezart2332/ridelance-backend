using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Echipa: proprietarul platformei, autentificarea în doi pași (TOTP) cu coduri de recuperare și
/// invitațiile prin link. Cel mai vechi admin existent devine proprietar, ca o bază cu date să aibă
/// din prima pe cineva care poate invita admini.
/// </summary>
public partial class AddStaffSecurity : Migration
{
    private const string Schema = "public";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "is_owner", schema: Schema, table: "users", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "two_factor_secret", schema: Schema, table: "users", type: "character varying(256)", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<string>(name: "two_factor_pending_secret", schema: Schema, table: "users", type: "character varying(256)", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "two_factor_enabled_at_utc", schema: Schema, table: "users", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<long>(name: "two_factor_last_step", schema: Schema, table: "users", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<int>(name: "two_factor_failed_attempts", schema: Schema, table: "users", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>(name: "two_factor_locked_until_utc", schema: Schema, table: "users", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "two_factor_challenge_hash", schema: Schema, table: "users", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "two_factor_challenge_expires_at_utc", schema: Schema, table: "users", type: "timestamp with time zone", nullable: true);

        migrationBuilder.CreateTable(
            name: "staff_invitations",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                is_bootstrap = table.Column<bool>(type: "boolean", nullable: false),
                invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                accepted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                accepted_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_staff_invitations", x => x.id);
                table.ForeignKey("fk_staff_invitations_users_invited_by_user_id", x => x.invited_by_user_id, "users", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_staff_invitations_users_accepted_user_id", x => x.accepted_user_id, "users", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_staff_invitations_token_hash", schema: Schema, table: "staff_invitations", column: "token_hash", unique: true);
        migrationBuilder.CreateIndex(name: "ix_staff_invitations_email", schema: Schema, table: "staff_invitations", column: "email");
        migrationBuilder.CreateIndex(name: "ix_staff_invitations_invited_by_user_id", schema: Schema, table: "staff_invitations", column: "invited_by_user_id");
        migrationBuilder.CreateIndex(name: "ix_staff_invitations_accepted_user_id", schema: Schema, table: "staff_invitations", column: "accepted_user_id");

        migrationBuilder.CreateTable(
            name: "two_factor_recovery_codes",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                used_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_two_factor_recovery_codes", x => x.id);
                table.ForeignKey("fk_two_factor_recovery_codes_users_user_id", x => x.user_id, "users", "id", Schema, onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(name: "ix_two_factor_recovery_codes_user_id", schema: Schema, table: "two_factor_recovery_codes", column: "user_id");

        migrationBuilder.Sql("""
            UPDATE public.users SET is_owner = TRUE
            WHERE id = (SELECT id FROM public.users WHERE role = 'Admin' AND deleted_at_utc IS NULL ORDER BY created_at_utc LIMIT 1);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "two_factor_recovery_codes", schema: Schema);
        migrationBuilder.DropTable(name: "staff_invitations", schema: Schema);
        foreach (string column in new[]
        {
            "is_owner", "two_factor_secret", "two_factor_pending_secret", "two_factor_enabled_at_utc", "two_factor_last_step",
            "two_factor_failed_attempts", "two_factor_locked_until_utc", "two_factor_challenge_hash", "two_factor_challenge_expires_at_utc",
        })
        {
            migrationBuilder.DropColumn(name: column, schema: Schema, table: "users");
        }
    }
}
