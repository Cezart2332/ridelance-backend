using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Mijloace fixe, după regulile din 2026:
    /// <list type="bullet">
    /// <item>pragul de mijloc fix urcă de la 2.500 la 5.000 lei (OUG 8/2026), pentru achizițiile de la
    /// 1 ianuarie 2026; regula veche rămâne pentru documentele dinainte;</item>
    /// <item>un activ poate fi marcat autoturism (durată 48–72 de luni);</item>
    /// <item>un activ poate avea un plafon deductibil lunar (art. 28 alin. 14 din Codul fiscal), iar
    /// fiecare linie de amortizare ține minte cât din ea se deduce. Liniile existente n-au plafon,
    /// deci se deduc integral, ca până acum.</item>
    /// </list>
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AssetVehicleRules : Migration
    {
        private const string Schema = "public";
        private const string Rule2026 = "b1c2d3e4-f5a6-4b7c-8d9e-0000000fa002";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_passenger_car", schema: Schema, table: "pfa_assets", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<decimal>(
                name: "monthly_deduction_cap", schema: Schema, table: "pfa_assets", type: "numeric(18,2)", nullable: true);
            migrationBuilder.AddColumn<decimal>(
                name: "deductible_amount", schema: Schema, table: "depreciation_lines", type: "numeric(18,2)", nullable: false, defaultValue: 0m);
            migrationBuilder.Sql("UPDATE public.depreciation_lines SET deductible_amount = amount;");

            // Regula nouă preia excluderile și începutul amortizării de la cea în vigoare.
            migrationBuilder.Sql($"""
                INSERT INTO public.fixed_asset_rules (id, threshold, depreciation_start, excluded_categories, valid_from, valid_to)
                SELECT '{Rule2026}', 5000, r.depreciation_start, r.excluded_categories, DATE '2026-01-01', NULL
                FROM public.fixed_asset_rules r
                WHERE r.valid_from < DATE '2026-01-01'
                  AND NOT EXISTS (SELECT 1 FROM public.fixed_asset_rules x WHERE x.valid_from >= DATE '2026-01-01')
                ORDER BY r.valid_from DESC
                LIMIT 1;
                """);
            migrationBuilder.Sql("""
                UPDATE public.fixed_asset_rules SET valid_to = DATE '2025-12-31'
                WHERE valid_to IS NULL AND valid_from < DATE '2026-01-01'
                  AND EXISTS (SELECT 1 FROM public.fixed_asset_rules x WHERE x.valid_from = DATE '2026-01-01');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM public.fixed_asset_rules WHERE id = '{Rule2026}';");
            migrationBuilder.Sql("UPDATE public.fixed_asset_rules SET valid_to = NULL WHERE valid_to = DATE '2025-12-31';");
            migrationBuilder.DropColumn(name: "deductible_amount", schema: Schema, table: "depreciation_lines");
            migrationBuilder.DropColumn(name: "monthly_deduction_cap", schema: Schema, table: "pfa_assets");
            migrationBuilder.DropColumn(name: "is_passenger_car", schema: Schema, table: "pfa_assets");
        }
    }
}
