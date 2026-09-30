using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// IBAN-ul se păstrează întreg, nu mascat: nu e o informație privată (e pe facturi) și contabilitatea
/// are nevoie de el complet, la declarații și la recunoașterea transferurilor între conturi (R43).
/// Valorile deja mascate le completează <c>IbanBackfillJob</c> la pornire, din IBAN-ul criptat al
/// declarației contului.
/// </summary>
public partial class StoreFullIban : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(name: "iban_masked", schema: "public", table: "bank_accounts", newName: "iban");
        migrationBuilder.RenameColumn(name: "iban_masked", schema: "public", table: "pfa_bank_account_declarations", newName: "iban");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(name: "iban", schema: "public", table: "bank_accounts", newName: "iban_masked");
        migrationBuilder.RenameColumn(name: "iban", schema: "public", table: "pfa_bank_account_declarations", newName: "iban_masked");
    }
}
