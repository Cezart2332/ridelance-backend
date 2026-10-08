using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Numerotarea împuternicirilor ANAF (<c>ANAF-000001</c>). Secvență, nu <c>MAX + 1</c>: două
    /// trimiteri simultane ale pasului fiscal nu primesc același număr.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddAnafMandateNumberSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS public.anaf_mandate_number_seq AS bigint START WITH 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS public.anaf_mandate_number_seq;");
        }
    }
}
