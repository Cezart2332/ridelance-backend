using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Resetarea parolei uitate: hash-ul codului trimis pe email, expirarea lui și încercările greșite.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddPasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "password_reset_code_hash",
                schema: "public",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "password_reset_code_expires_at_utc",
                schema: "public",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "password_reset_attempts",
                schema: "public",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "password_reset_code_hash", schema: "public", table: "users");
            migrationBuilder.DropColumn(name: "password_reset_code_expires_at_utc", schema: "public", table: "users");
            migrationBuilder.DropColumn(name: "password_reset_attempts", schema: "public", table: "users");
        }
    }
}
