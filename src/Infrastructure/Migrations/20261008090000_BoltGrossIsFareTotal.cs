using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Rezumatul lunar Bolt: venitul brut e doar TOTAL-ul de la „Defalcare tarif”. Extracțiile citite
    /// cu regula de ieri (tarif + alte venituri) primesc înapoi TOTAL-ul de tarif, componenta salvată
    /// sub eticheta „Total tarif curse”. „Alte venituri” intră în venit doar la confirmarea băncii.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class BoltGrossIsFareTotal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.document_extractions AS e
                SET amount = fare.value
                FROM (
                    SELECT x.id, (item ->> 'amount')::numeric(18,2) AS value
                    FROM public.document_extractions AS x,
                         jsonb_array_elements(x.other_amounts_json) AS item
                    WHERE jsonb_typeof(x.other_amounts_json) = 'array'
                      AND item ->> 'label' = 'Total tarif curse'
                ) AS fare
                WHERE e.id = fare.id
                  AND e.amount IS DISTINCT FROM fare.value;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Regula veche (tarif + alte venituri) nu se reface: brutul corect e TOTAL-ul de tarif.
        }
    }
}
