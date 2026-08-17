using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Locațiile de sediu social puse la dispoziție de Consulto, pentru șoferii care nu au unde
    /// să declare sediul. Tabelul exista, dar era gol: ecranul „Folosesc o adresă pusă la
    /// dispoziție de Consulto" nu avea ce oferi.
    ///
    /// Zona intră în <c>adresa_localitate</c>, iar județul în <c>adresa_judet</c> — strada rămâne
    /// goală pentru că oferta e pe zonă, nu pe o adresă anume; adresa exactă o comunică Consulto
    /// la semnarea contractului de găzduire.
    ///
    /// Tariful se stochează (39900 bani = 399 lei/an), dar NU se afișează în onboarding: la pasul
    /// de alegere a zonei prețul ar fi zgomot, iar plata are ecranul ei.
    ///
    /// Id-urile sunt UUID v5 derivate din județ + zonă, ca o re-rulare să nu dubleze locațiile.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class SeedConsultoOffices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO public.consulto_offices
                    (id, adresa_judet, adresa_localitate, position, monthly_fee_bani, yearly_fee_bani, is_active, created_at_utc, updated_at_utc)
                VALUES
                    ('75f93f0c-2cb8-546e-8d8d-2edb27fa9f91', 'București', 'Sectorul 1', 0, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('caa44ab6-16cf-599b-82f0-8729d469292e', 'București', 'Sectorul 2', 1, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('0d0f17d8-ba3f-55cf-8c6a-63a6c7f2fec3', 'București', 'Sectorul 3', 2, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('43f9f418-6074-58b1-86db-f8c246899aa1', 'București', 'Sectorul 4', 3, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('cedc537c-6c5c-5a50-969e-da3607935485', 'București', 'Sectorul 5', 4, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('ac0afc8a-13c7-5b49-a378-df48fdea372c', 'București', 'Sectorul 6', 5, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('144d30f8-92d1-5a46-9005-ce582d877baf', 'Ilfov', 'Ilfov', 6, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('4ad4a8b5-7d19-5ff2-950b-4468b6cdc98e', 'Cluj', 'Cluj-Napoca', 7, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('e7f6061a-c08a-5087-ace5-12a7a8bd149e', 'Timiș', 'Timișoara', 8, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('55f4a377-a13c-5d7c-874f-f508736eac72', 'Iași', 'Iași', 9, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('ebe77241-6c7d-5bbb-9162-3d5188dcf1dd', 'Brașov', 'Brașov', 10, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('3d4baac2-9e5d-56aa-bff3-5572f85cdb5f', 'Constanța', 'Constanța', 11, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('ade4806c-36f7-52be-b90c-2a7a1ef9c81c', 'Suceava', 'Suceava', 12, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00'),
                    ('0ff23113-0805-5a93-914d-fb5a41aa7b25', 'Botoșani', 'Botoșani', 13, 0, 39900, true, TIMESTAMPTZ '2026-08-17 00:00:00+00', TIMESTAMPTZ '2026-08-17 00:00:00+00')
                ON CONFLICT (id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM public.consulto_offices
                WHERE id IN (
                    '75f93f0c-2cb8-546e-8d8d-2edb27fa9f91',
                    'caa44ab6-16cf-599b-82f0-8729d469292e',
                    '0d0f17d8-ba3f-55cf-8c6a-63a6c7f2fec3',
                    '43f9f418-6074-58b1-86db-f8c246899aa1',
                    'cedc537c-6c5c-5a50-969e-da3607935485',
                    'ac0afc8a-13c7-5b49-a378-df48fdea372c',
                    '144d30f8-92d1-5a46-9005-ce582d877baf',
                    '4ad4a8b5-7d19-5ff2-950b-4468b6cdc98e',
                    'e7f6061a-c08a-5087-ace5-12a7a8bd149e',
                    '55f4a377-a13c-5d7c-874f-f508736eac72',
                    'ebe77241-6c7d-5bbb-9162-3d5188dcf1dd',
                    '3d4baac2-9e5d-56aa-bff3-5572f85cdb5f',
                    'ade4806c-36f7-52be-b90c-2a7a1ef9c81c',
                    '0ff23113-0805-5a93-914d-fb5a41aa7b25'
                );
                """);
        }
    }
}
