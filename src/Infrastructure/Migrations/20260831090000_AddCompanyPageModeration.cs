using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Mini-site-ul firmei nu mai ajunge public de la sine: se salvează ca ciornă, iar
    /// administrarea decide dacă versiunea pleacă mai departe.
    ///
    /// Două coloane jsonb, lângă restul personalizării paginii. <c>page_moderation</c> ține
    /// verdictul și secțiunile blocate; <c>published_page</c> ține copia aprobată — cea pe care o
    /// citește pagina publică. Copia separată e ce face ca prima literă tastată după o aprobare să
    /// nu scoată pagina de pe internet: ciorna se schimbă, versiunea live rămâne.
    ///
    /// Rândurile existente nu se pot considera verificate — nimeni nu s-a uitat până acum peste
    /// ele. Cele care au conținut intră în coadă ca „în așteptare", nu ca aprobate, iar până la
    /// verdict pagina lor arată doar denumirea, mașinile aprobate și datele de contact marcate
    /// publice. Un <c>UPDATE</c> care le-ar fi aprobat în bloc ar fi publicat exact textele pentru
    /// care s-a introdus verificarea.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCompanyPageModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "page_moderation",
                schema: "public",
                table: "company_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "published_page",
                schema: "public",
                table: "company_profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            // `Status = 1` e `CompanyPageReviewStatus.Pending`. Enumerațiile se serializează ca
            // numere cu opțiunile implicite din `CompanyProfileConfiguration`, iar numele de
            // proprietăți rămân PascalCase — schimbarea oricăreia dintre cele două convenții cere
            // și schimbarea instrucțiunii de aici.
            migrationBuilder.Sql(
                """
                UPDATE public.company_profiles
                SET page_moderation = jsonb_build_object(
                        'Status', 1,
                        'BlockedSections', '[]'::jsonb,
                        'SubmittedAtUtc', to_jsonb(now() AT TIME ZONE 'utc'))
                WHERE coalesce(tagline, '') <> ''
                   OR coalesce(public_description, '') <> ''
                   OR coalesce(cover_image_url, '') <> ''
                   OR coalesce(pickup_address, '') <> ''
                   OR coalesce(pickup_note, '') <> ''
                   OR jsonb_array_length(coalesce(page_content -> 'Highlights', '[]'::jsonb)) > 0
                   OR jsonb_array_length(coalesce(page_content -> 'Schedule', '[]'::jsonb)) > 0
                   OR jsonb_array_length(coalesce(page_content -> 'CoverageAreas', '[]'::jsonb)) > 0
                   OR jsonb_array_length(coalesce(page_content -> 'Faq', '[]'::jsonb)) > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "page_moderation",
                schema: "public",
                table: "company_profiles");

            migrationBuilder.DropColumn(
                name: "published_page",
                schema: "public",
                table: "company_profiles");
        }
    }
}
