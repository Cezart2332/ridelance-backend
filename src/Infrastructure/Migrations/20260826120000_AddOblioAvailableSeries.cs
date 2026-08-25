using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// <c>available_series</c> pe <c>oblio_integrations</c> — seriile de facturare din contul Oblio.
    ///
    /// Se citeau la conectare și se aruncau: pagina de facturi le primea mereu goale, deci lista
    /// de serii din formularul de emitere nu avea ce afișa. Acum se rețin.
    ///
    /// Fără backfill — nu putem inventa seriile cuiva. Integrările existente rămân cu coloana
    /// goală până la o reconectare, care le recitește din Oblio.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddOblioAvailableSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "available_series",
                schema: "public",
                table: "oblio_integrations",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "available_series", schema: "public", table: "oblio_integrations");
        }
    }
}
