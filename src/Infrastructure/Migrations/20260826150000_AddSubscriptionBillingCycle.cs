using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Abonamentele trec de la săptămânal la lunar, cu variantă anuală.
    ///
    /// Ciclul de facturare devine o alegere a clientului, deci trebuie ținut minte: până acum
    /// exista un singur ciclu — săptămânal, luni la 15:00 — și nu avea ce stoca nimeni.
    ///
    /// Rândurile existente primesc `Monthly`: sunt abonamente vândute pe ciclul săptămânal, iar
    /// dintre cele două valori posibile lunar e cel apropiat. Reînnoirea reală o dictează oricum
    /// Stripe, pe abonamentul deja creat acolo; coloana descrie ce a cumpărat clientul.
    ///
    /// Al doilea lucru: statusurile scrise de mecanismul de deblocare de luni 15:00, care dispare.
    /// `PaidPendingAccess` însemna „plătit, dar accesul vine luni” — o stare care nu mai există,
    /// deci rândurile pe ea devin `Active`. `ActivePendingBilling` rămâne pe loc: e tot un
    /// abonament valid, iar codul îl tratează la fel ca `Active`.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddSubscriptionBillingCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "billing_cycle",
                schema: "public",
                table: "user_subscriptions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Monthly");

            // Accesul nu mai e acordat de un job: cine a plătit are acces. Rândurile care așteptau
            // rularea de luni ar rămâne altfel blocate pe o poartă care nu mai există.
            migrationBuilder.Sql(
                """
                UPDATE public.user_subscriptions
                SET status = 'Active'
                WHERE status = 'PaidPendingAccess';
                """);

            migrationBuilder.Sql(
                """
                UPDATE public.user_subscriptions
                SET dashboard_access_granted = TRUE,
                    dashboard_access_granted_utc = COALESCE(dashboard_access_granted_utc, NOW() AT TIME ZONE 'utc')
                WHERE dashboard_access_granted = FALSE
                  AND status IN ('Active', 'ActivePendingBilling');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "billing_cycle",
                schema: "public",
                table: "user_subscriptions");
        }
    }
}
