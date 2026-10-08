using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Zona MRZ a buletinului e un câmp de control: încrederea modelului pe transcrierea ei nu mai
    /// trimite documentul la verificare manuală (cifrele de control o verifică în cod). Buletinele
    /// marcate „de verificat” doar din cauza ei — fără motiv de suspiciune, cu data expirării citită
    /// și fără alt câmp nesigur — își pierd marcajul.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class MrzDoesNotForceReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.documents AS d
                SET ai_requires_manual_review = false
                WHERE d.ai_requires_manual_review
                  AND d.ai_suspicion_reasons IS NULL
                  AND d.ai_extracted_expires_at_utc IS NOT NULL
                  AND d.category IN ('CarteIdentitate', 'Buletin', 'CeiReaderPdf')
                  AND EXISTS (
                      SELECT 1 FROM public.extracted_fields f
                      WHERE f.document_id = d.id AND f.field_key = 'mrz_raw' AND f.review_state = 'NeedsManualReview')
                  AND NOT EXISTS (
                      SELECT 1 FROM public.extracted_fields f
                      WHERE f.document_id = d.id AND f.field_key <> 'mrz_raw' AND f.review_state = 'NeedsManualReview');

                UPDATE public.extracted_fields
                SET review_state = 'Auto'
                WHERE field_key = 'mrz_raw' AND review_state = 'NeedsManualReview';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Marcajul pus din cauza MRZ-ului era greșit; nu se reface.
        }
    }
}
