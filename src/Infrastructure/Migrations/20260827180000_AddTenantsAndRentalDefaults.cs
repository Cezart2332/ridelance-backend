using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Chiriașul devine entitate, închirierea primește ce-i lipsea, iar firma primește valori
    /// implicite care chiar se salvează.
    ///
    /// Backfill-ul creează câte un chiriaș pentru fiecare închiriere existentă, fără să încerce să
    /// unească rândurile după nume. Doi oameni pot avea același nume; unindu-i, contractul unuia ar
    /// ajunge cu datele celuilalt. Dedublarea, dacă se dorește, e o decizie a proprietarului, nu a
    /// unei migrații.
    ///
    /// Codul public pornește de la numărul închirierilor existente, ca rândurile vechi să primească
    /// numere fără să se ciocnească de cele viitoare.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddTenantsAndRentalDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    cnp = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    id_series = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    id_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    cui = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    reg_com = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    driver_license_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("pk_tenants", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_tenants_owner_user_id_name",
                schema: "public",
                table: "tenants",
                columns: ["owner_user_id", "name"]);

            migrationBuilder.CreateTable(
                name: "fleet_rental_defaults",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weekly_rent_bani = table.Column<long>(type: "bigint", nullable: true),
                    deposit_bani = table.Column<long>(type: "bigint", nullable: true),
                    min_period_days = table.Column<int>(type: "integer", nullable: true),
                    has_km_limit = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    mileage_limit = table.Column<int>(type: "integer", nullable: true),
                    extra_km_cost_bani = table.Column<long>(type: "bigint", nullable: true),
                    fuel_rule = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    default_conditions = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("pk_fleet_rental_defaults", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_fleet_rental_defaults_owner_user_id",
                schema: "public",
                table: "fleet_rental_defaults",
                column: "owner_user_id",
                unique: true);

            // Coloanele noi pe închiriere, toate cu valori care lasă rândurile vechi valide.
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "public",
                table: "rentals",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<string>(
                name: "public_code",
                schema: "public",
                table: "rentals",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "lifecycle",
                schema: "public",
                table: "rentals",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Confirmed");

            migrationBuilder.AddColumn<long>(
                name: "other_costs_bani",
                schema: "public",
                table: "rentals",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "mileage_limit",
                schema: "public",
                table: "rentals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fuel_level_at_pickup",
                schema: "public",
                table: "rentals",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "accessories_other",
                schema: "public",
                table: "rentals",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            // Câte un chiriaș per închiriere, din datele denormalizate. `fiscal_code` ținea și CNP,
            // și CUI, într-o singură coloană — se desparte după tipul chiriașului.
            migrationBuilder.Sql(
                """
                INSERT INTO public.tenants
                    (id, owner_user_id, type, name, cnp, cui, phone, email, created_at_utc, updated_at_utc)
                SELECT
                    gen_random_uuid(),
                    r.owner_user_id,
                    r.tenant_type,
                    r.tenant_name,
                    CASE WHEN r.tenant_type = 'Individual' THEN r.tenant_fiscal_code END,
                    CASE WHEN r.tenant_type <> 'Individual' THEN r.tenant_fiscal_code END,
                    r.tenant_phone,
                    r.tenant_email,
                    r.created_at_utc,
                    r.updated_at_utc
                FROM public.rentals r;
                """);

            // Legarea se face pe perechea (proprietar, nume, moment de creare), care e unică:
            // fiecare rând de mai sus provine dintr-o singură închiriere.
            migrationBuilder.Sql(
                """
                UPDATE public.rentals r
                SET tenant_id = t.id
                FROM public.tenants t
                WHERE t.owner_user_id = r.owner_user_id
                  AND t.name = r.tenant_name
                  AND t.created_at_utc = r.created_at_utc;
                """);

            migrationBuilder.Sql(
                """
                UPDATE public.rentals
                SET accessories = COALESCE(
                    CASE WHEN accessories IS NULL OR btrim(accessories) = ''
                         THEN '[]'
                         ELSE to_jsonb(ARRAY[accessories])::text
                    END, '[]');
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE public.rentals
                ALTER COLUMN accessories TYPE jsonb USING accessories::jsonb,
                ALTER COLUMN accessories SET DEFAULT '[]'::jsonb,
                ALTER COLUMN accessories SET NOT NULL;
                """);

            // Secvența pornește după rândurile existente, ca numerotarea să nu se repete.
            migrationBuilder.Sql(
                """
                CREATE SEQUENCE IF NOT EXISTS public.rental_public_code_seq AS bigint START WITH 1;
                SELECT setval('public.rental_public_code_seq', GREATEST((SELECT COUNT(*) FROM public.rentals), 1), true);
                """);

            migrationBuilder.Sql(
                """
                WITH numbered AS (
                    SELECT id, row_number() OVER (ORDER BY created_at_utc, id) AS n
                    FROM public.rentals
                )
                UPDATE public.rentals r
                SET public_code = 'RL-' || lpad(numbered.n::text, 6, '0')
                FROM numbered
                WHERE numbered.id = r.id;
                """);

            migrationBuilder.DropColumn(name: "tenant_name", schema: "public", table: "rentals");
            migrationBuilder.DropColumn(name: "tenant_type", schema: "public", table: "rentals");
            migrationBuilder.DropColumn(name: "tenant_fiscal_code", schema: "public", table: "rentals");
            migrationBuilder.DropColumn(name: "tenant_phone", schema: "public", table: "rentals");
            migrationBuilder.DropColumn(name: "tenant_email", schema: "public", table: "rentals");

            migrationBuilder.CreateIndex(
                name: "ix_rentals_public_code",
                schema: "public",
                table: "rentals",
                column: "public_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rentals_tenant_id",
                schema: "public",
                table: "rentals",
                column: "tenant_id");

            migrationBuilder.AddForeignKey(
                name: "fk_rentals_tenants_tenant_id",
                schema: "public",
                table: "rentals",
                column: "tenant_id",
                principalSchema: "public",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tenant_name", schema: "public", table: "rentals",
                type: "character varying(256)", maxLength: 256, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "tenant_type", schema: "public", table: "rentals",
                type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Individual");
            migrationBuilder.AddColumn<string>(
                name: "tenant_fiscal_code", schema: "public", table: "rentals",
                type: "character varying(32)", maxLength: 32, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "tenant_phone", schema: "public", table: "rentals",
                type: "character varying(32)", maxLength: 32, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "tenant_email", schema: "public", table: "rentals",
                type: "character varying(256)", maxLength: 256, nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE public.rentals r
                SET tenant_name = t.name,
                    tenant_type = t.type,
                    tenant_fiscal_code = COALESCE(t.cnp, t.cui),
                    tenant_phone = t.phone,
                    tenant_email = t.email
                FROM public.tenants t
                WHERE t.id = r.tenant_id;
                """);

            migrationBuilder.DropForeignKey(name: "fk_rentals_tenants_tenant_id", schema: "public", table: "rentals");
            migrationBuilder.DropIndex(name: "ix_rentals_tenant_id", schema: "public", table: "rentals");
            migrationBuilder.DropIndex(name: "ix_rentals_public_code", schema: "public", table: "rentals");

            migrationBuilder.Sql(
                """
                ALTER TABLE public.rentals
                ALTER COLUMN accessories DROP DEFAULT,
                ALTER COLUMN accessories DROP NOT NULL,
                ALTER COLUMN accessories TYPE character varying(1024)
                    USING NULLIF(btrim(accessories::text, '[]"'), '');
                """);

            migrationBuilder.DropColumn(name: "tenant_id", schema: "public", table: "rentals");
            migrationBuilder.DropColumn(name: "public_code", schema: "public", table: "rentals");
            migrationBuilder.DropColumn(name: "lifecycle", schema: "public", table: "rentals");
            migrationBuilder.DropColumn(name: "other_costs_bani", schema: "public", table: "rentals");
            migrationBuilder.DropColumn(name: "mileage_limit", schema: "public", table: "rentals");
            migrationBuilder.DropColumn(name: "fuel_level_at_pickup", schema: "public", table: "rentals");
            migrationBuilder.DropColumn(name: "accessories_other", schema: "public", table: "rentals");

            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS public.rental_public_code_seq;");

            migrationBuilder.DropTable(name: "fleet_rental_defaults", schema: "public");
            migrationBuilder.DropTable(name: "tenants", schema: "public");
        }
    }
}
