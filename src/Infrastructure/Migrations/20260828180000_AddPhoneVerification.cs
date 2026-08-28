using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Confirmarea numărului de telefon, după tiparul confirmării de email.
    ///
    /// Conturile existente rămân neconfirmate — nu se poate presupune că un număr tastat cândva
    /// într-un formular e al celui care l-a tastat; exact asta stabilește confirmarea.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddPhoneVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "phone_verified_at_utc",
                schema: "public",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone_verification_code",
                schema: "public",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "phone_verification_code_expires_at_utc",
                schema: "public",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "phone_verification_attempts",
                schema: "public",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "phone_verification_attempts", schema: "public", table: "users");
            migrationBuilder.DropColumn(name: "phone_verification_code_expires_at_utc", schema: "public", table: "users");
            migrationBuilder.DropColumn(name: "phone_verification_code", schema: "public", table: "users");
            migrationBuilder.DropColumn(name: "phone_verified_at_utc", schema: "public", table: "users");
        }
    }
}
