using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Recomandările: codul fiecărui client, cine l-a recomandat pe cine și invitațiile trimise pe
    /// email. Codul conturilor vechi se generează la prima cerere, nu aici.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddReferrals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "referral_code",
                schema: "public",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "referred_by_user_id",
                schema: "public",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_referral_code",
                schema: "public",
                table: "users",
                column: "referral_code",
                unique: true,
                filter: "referral_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_referred_by_user_id",
                schema: "public",
                table: "users",
                column: "referred_by_user_id");

            migrationBuilder.CreateTable(
                name: "referral_invitations",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inviter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_referral_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_referral_invitations_users_inviter_user_id",
                        column: x => x.inviter_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_referral_invitations_inviter_user_id_email",
                schema: "public",
                table: "referral_invitations",
                columns: new[] { "inviter_user_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_referral_invitations_email",
                schema: "public",
                table: "referral_invitations",
                column: "email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "referral_invitations", schema: "public");
            migrationBuilder.DropIndex(name: "ix_users_referral_code", schema: "public", table: "users");
            migrationBuilder.DropIndex(name: "ix_users_referred_by_user_id", schema: "public", table: "users");
            migrationBuilder.DropColumn(name: "referral_code", schema: "public", table: "users");
            migrationBuilder.DropColumn(name: "referred_by_user_id", schema: "public", table: "users");
        }
    }
}
