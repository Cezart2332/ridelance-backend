using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Cursele aduse prin Bolt Fleet API 2.0 au rămas cu comisionul și netul goale. Le completăm ca la
    /// import: comision 25% din brut fără bacșiș (Domain.Bolt.BoltCommission), net = brut − comision.
    /// Cursele V1, care au venit cu comisionul real, nu se ating.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class BoltEstimatedCommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.bolt_orders
                SET commission = round(greatest(ride_price - tip, 0) * 0.25, 2)
                WHERE commission IS NULL;
                """);
            migrationBuilder.Sql("""
                UPDATE public.bolt_orders
                SET net_earnings = ride_price - commission
                WHERE net_earnings IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nu se mai pot deosebi de cursele V1; valorile calculate rămân.
        }
    }
}
