using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Are deja cont de șofer Uber/Bolt: atunci își corectează singur emailul, telefonul și numele;
    /// altfel îl deschidem noi pe datele contului RIDElance. Null pe dosarele vechi.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddDriverHasExistingAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "driver_has_existing_account",
                schema: "public",
                table: "pfa_platform_accounts",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "driver_has_existing_account", schema: "public", table: "pfa_platform_accounts");
        }
    }
}
