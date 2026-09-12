using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// <c>psu_ip_address</c> pe conexiunile bancare.
    ///
    /// Smart Accounts cere antetul <c>PSU-IP-Address</c> la FIECARE apel pe un consimțământ, nu
    /// doar la deschiderea lui — verificat pe sandbox: fără el răspunde 400 „EMPTY OR MISSING
    /// PSU-IP-ADDRESS" și la citirea stării, și la conturi. Jobul de sincronizare n-are cerere
    /// HTTP din care să-l ia, deci se păstrează pe rândul conexiunii.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddBankConnectionPsuIp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "psu_ip_address",
                schema: "public",
                table: "bank_connections",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "psu_ip_address", schema: "public", table: "bank_connections");
        }
    }
}
