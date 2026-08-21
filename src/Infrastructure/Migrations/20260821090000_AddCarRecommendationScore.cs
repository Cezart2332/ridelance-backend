using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Scorul „Recomandate" pe anunț (spec §5.2, §7.1).
    ///
    /// Stocat, nu calculat la cerere: sortarea listei publice nu are voie să depindă de un calcul
    /// per rând. <c>score_computed_at_utc</c> rămâne NULL până la primul calcul, ca jobul nocturn
    /// să poată distinge „scor zero" de „niciodată calculat".
    ///
    /// Indexul repetă exact ordinea din <c>ORDER BY</c> — <c>score DESC, updated_at DESC, id</c>,
    /// după coloanele de filtrare. Cu altă ordine, Postgres l-ar folosi doar ca să filtreze și ar
    /// sorta oricum în memorie.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCarRecommendationScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "recommendation_score",
                schema: "public",
                table: "cars",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "score_computed_at_utc",
                schema: "public",
                table: "cars",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_cars_recommended
                    ON public.cars (active, approval_status, status, recommendation_score DESC, updated_at_utc DESC, id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS public.ix_cars_recommended;");

            migrationBuilder.DropColumn(name: "recommendation_score", schema: "public", table: "cars");
            migrationBuilder.DropColumn(name: "score_computed_at_utc", schema: "public", table: "cars");
        }
    }
}
