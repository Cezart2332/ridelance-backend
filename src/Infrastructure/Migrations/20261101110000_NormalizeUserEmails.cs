using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Emailurile conturilor trec în forma în care se caută acum: fără spații, cu litere mici.
    ///
    /// Conturile care ar ajunge la aceeași adresă („Ion@x.ro” și „ion@x.ro”) nu se ating: unirea lor
    /// e o decizie de om, nu de migrație, iar indexul unic pe email ar opri oricum actualizarea.
    /// Le găsește interogarea din comentariul de mai jos.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    /// <remarks>
    /// Duplicatele rămase:
    /// <code>
    /// SELECT lower(btrim(email)) AS adresa, array_agg(email ORDER BY created_at_utc) AS conturi
    /// FROM public.users GROUP BY lower(btrim(email)) HAVING count(*) > 1;
    /// </code>
    /// </remarks>
    public partial class NormalizeUserEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.users AS u
                SET email = lower(btrim(u.email))
                WHERE u.email <> lower(btrim(u.email))
                  AND NOT EXISTS (
                      SELECT 1 FROM public.users AS o
                      WHERE o.id <> u.id AND lower(btrim(o.email)) = lower(btrim(u.email)));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forma veche (literele mari) nu se mai știe; adresele rămân cum sunt.
        }
    }
}
