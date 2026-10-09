using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Codul de confirmare a telefonului nu mai stă la noi: îl generează, îl trimite și îl verifică
    /// Twilio Verify. Coloana rămasă ar ține doar coduri vechi, deja inutile.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class DropPhoneVerificationCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE public.users DROP COLUMN IF EXISTS phone_verification_code;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE public.users ADD COLUMN IF NOT EXISTS phone_verification_code character varying(16);");
        }
    }
}
