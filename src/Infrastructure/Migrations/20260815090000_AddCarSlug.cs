using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Identitatea publică a unui anunț, pentru pagina de detaliu (`/masini/{slug}`).
    ///
    /// Backfill-ul repetă în SQL ce face `CarSlug.Generate`: diacriticele se traduc, restul devine
    /// cratimă, iar la final se adaugă patru caractere din Id. Dacă două anunțuri ies totuși cu
    /// același slug (aceeași mașină, sufix identic), al doilea primește Id-ul întreg — indexul unic
    /// vine imediat după și nu are voie să pice la deploy.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCarSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "slug",
                schema: "public",
                table: "cars",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE public.cars
                SET slug = trim(BOTH '-' FROM regexp_replace(
                        translate(
                            lower(brand || ' ' || model || ' ' || year::text),
                            'ăâîșşțţáàäéèêíóöúüçñšž',
                            'aaissttaaaeeeioouucnsz'),
                        '[^a-z0-9]+', '-', 'g'))
                    || '-' || left(replace(id::text, '-', ''), 4);
                """);

            migrationBuilder.Sql("""
                WITH dupes AS (
                    SELECT id, row_number() OVER (PARTITION BY slug ORDER BY created_at_utc, id) AS rn
                    FROM public.cars)
                UPDATE public.cars c
                SET slug = left(c.slug, 120) || '-' || replace(c.id::text, '-', '')
                FROM dupes d
                WHERE d.id = c.id AND d.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_cars_slug",
                schema: "public",
                table: "cars",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ix_cars_slug", schema: "public", table: "cars");
            migrationBuilder.DropColumn(name: "slug", schema: "public", table: "cars");
        }
    }
}
