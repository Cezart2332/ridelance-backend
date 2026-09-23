using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// „Am deja pus deoparte” a ieșit din card, iar motorul nu mai scade suma salvată. Rulările
    /// PFA-urilor care o completaseră se refac acum, nu abia a doua zi. Coloana rămâne, cu datele.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class RecalculateWithoutExistingReserve : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.fiscal_estimate_runs r
                SET stale = TRUE, stale_since_utc = NOW() - INTERVAL '1 hour'
                WHERE NOT r.stale
                  AND EXISTS (
                      SELECT 1 FROM public.pfa_tax_profiles p
                      WHERE p.pfa_registration_id = r.pfa_registration_id
                        AND p.tax_year = r.tax_year
                        AND p.existing_reserve IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nimic de refăcut: rulările expirate se recalculează oricum, iar datele n-au fost atinse.
        }
    }
}
