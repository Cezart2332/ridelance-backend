using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Codul de TVA al Uber B.V. din seed-ul B0 era greșit (<c>NL852071588B01</c>); pe facturile reale
    /// e <c>NL852071589B01</c>, iar cu cel greșit orice factură Uber ieșea „furnizor necunoscut”.
    /// Dacă furnizorul corect a fost deja adăugat de mână, rândul greșit rămâne pentru ștergere din
    /// Reguli fiscale (altfel ar rezulta doi furnizori cu același cod).
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class FixUberVatId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.supplier_tax_profiles
                SET vat_id = 'NL852071589B01'
                WHERE vat_id = 'NL852071588B01'
                  AND NOT EXISTS (SELECT 1 FROM public.supplier_tax_profiles WHERE vat_id = 'NL852071589B01');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Codul corect nu se mai strică la loc.
        }
    }
}
