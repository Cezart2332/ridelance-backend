using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// AI-ul doar citește documentele (OCR) și nu mai dă verdict: <c>Passed</c> și <c>Failed</c>
    /// devin <c>Read</c>. Actele respinse doar de verificarea automată (fără motiv scris de un om)
    /// revin la <c>Pending</c>, ca să le judece echipa; cele respinse de admin sau contabil rămân.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class DocumentAiReadOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.documents
                SET status = 'Pending'
                WHERE ai_status = 'Failed' AND status = 'Rejected' AND review_note IS NULL;

                UPDATE public.documents
                SET ai_status = 'Read', ai_summary = NULL
                WHERE ai_status IN ('Passed', 'Failed');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Verdictul vechi nu se mai știe: actele citite redevin „trecute”.
            migrationBuilder.Sql("UPDATE public.documents SET ai_status = 'Passed' WHERE ai_status = 'Read';");
        }
    }
}
