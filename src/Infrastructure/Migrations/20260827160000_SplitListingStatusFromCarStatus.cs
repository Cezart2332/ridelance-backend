using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Anunțul și mașina primesc stări separate.
    ///
    /// Până acum exista un singur comutator, `active`. În el ajungeau trei lucruri diferite:
    /// „încă nepublicat", „retras temporar de proprietar" și „scos definitiv" — toate `false`,
    /// imposibil de deosebit. Iar singura cale de a scoate o mașină din flotă era ștergerea, care
    /// lua cu ea închirierile și dosarul.
    ///
    /// Backfill-ul urmează exact regula după care se aprindea `active` în cod: un anunț era vizibil
    /// doar aprobat și plătit. Ce era `active = true` devine `Published`; restul devine `Draft`,
    /// nu `Paused` — o pauză înseamnă că cineva a retras ceva ce fusese publicat, iar despre
    /// rândurile astea nu știm asta.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class SplitListingStatusFromCarStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cars_recommended",
                schema: "public",
                table: "cars");

            migrationBuilder.AddColumn<string>(
                name: "listing_status",
                schema: "public",
                table: "cars",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.Sql(
                """
                UPDATE public.cars
                SET listing_status = CASE WHEN active THEN 'Published' ELSE 'Draft' END;
                """);

            migrationBuilder.DropColumn(
                name: "active",
                schema: "public",
                table: "cars");

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_cars_recommended
                ON public.cars (listing_status, approval_status, payment_status, status, recommendation_score DESC, updated_at_utc DESC, id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS public.ix_cars_recommended;");

            migrationBuilder.AddColumn<bool>(
                name: "active",
                schema: "public",
                table: "cars",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE public.cars
                SET active = (listing_status = 'Published');
                """);

            migrationBuilder.DropColumn(
                name: "listing_status",
                schema: "public",
                table: "cars");

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_cars_recommended
                ON public.cars (active, approval_status, status, recommendation_score DESC, updated_at_utc DESC, id);
                """);
        }
    }
}
