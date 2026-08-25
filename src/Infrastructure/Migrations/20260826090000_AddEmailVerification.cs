using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Codul de confirmare a emailului, pe <c>users</c>.
    ///
    /// Coloanele sunt toate nullable și fără valoare implicită, iar conturile existente rămân cu
    /// <c>email_verified_at_utc</c> gol. Nu e o scăpare: confirmarea nu e impusă nicăieri, deci un
    /// backfill care le-ar fi marcat pe toate ca verificate ar fi scris o afirmație pe care nimeni
    /// n-a verificat-o. Când regula devine obligatorie, tot atunci se decide și ce se face cu ele.
    ///
    /// Codul se ține în clar. E de șase cifre, valabil o jumătate de oră și limitat la cinci
    /// încercări — un hash aici ar fi costat la fiecare verificare fără să schimbe ce poate face
    /// cineva cu acces la baza de date, care oricum are emailul și poate cere altul.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "email_verified_at_utc",
                schema: "public",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_verification_code",
                schema: "public",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "email_verification_code_expires_at_utc",
                schema: "public",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "email_verification_attempts",
                schema: "public",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "email_verification_attempts", schema: "public", table: "users");
            migrationBuilder.DropColumn(name: "email_verification_code_expires_at_utc", schema: "public", table: "users");
            migrationBuilder.DropColumn(name: "email_verification_code", schema: "public", table: "users");
            migrationBuilder.DropColumn(name: "email_verified_at_utc", schema: "public", table: "users");
        }
    }
}
