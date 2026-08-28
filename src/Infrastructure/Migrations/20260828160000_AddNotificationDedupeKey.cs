using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Cheia de deduplicare a notificărilor iese din textul citit de om.
    ///
    /// Până acum stătea între paranteze drepte, la finalul mesajului, fiindcă entitatea n-avea
    /// unde altundeva s-o pună: „Documentul tău expiră în 30 de zile. [expiry:8f3a…:30d:2026-08-28]".
    /// Utilizatorul o citea odată cu mesajul.
    ///
    /// Migrația mută eticheta din text în coloană și curăță textele existente. Fără mutare, joburile
    /// de expirare ar fi retrimis o dată toate notificările deja trimise, pentru că verificarea se
    /// face de acum pe coloană.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddNotificationDedupeKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dedupe_key",
                schema: "public",
                table: "notifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE public.notifications
                SET dedupe_key = substring(text from '\[(expiry:[^\]]+)\]')
                WHERE text LIKE '%[expiry:%';
                """);

            migrationBuilder.Sql(
                """
                UPDATE public.notifications
                SET text = btrim(regexp_replace(text, '\s*\[expiry:[^\]]+\]', ''))
                WHERE text LIKE '%[expiry:%';
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_notifications_dedupe_key
                ON public.notifications (dedupe_key)
                WHERE dedupe_key IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS public.ix_notifications_dedupe_key;");

            migrationBuilder.DropColumn(
                name: "dedupe_key",
                schema: "public",
                table: "notifications");
        }
    }
}
