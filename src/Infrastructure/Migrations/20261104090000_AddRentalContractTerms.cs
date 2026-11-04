using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Locurile din contractul de închiriere care rămâneau mereu linii goale, pentru că nu aveam
    /// unde ține valoarea: calitatea reprezentantului firmei, reprezentantul chiriașului-firmă, ziua
    /// și modalitatea de plată, penalitățile, franșiza, preavizul, chiria pe durata indisponibilității,
    /// conducătorul auto suplimentar și, pentru procesele-verbale, cheile, documentele predate și
    /// avariile.
    ///
    /// Termenii contractuali stau și pe setările flotei (se propun), și pe închiriere (se semnează),
    /// din același motiv ca chiria și garanția: setările se schimbă, contractul semnat nu.
    ///
    /// Toate coloanele sunt opționale: închirierile existente rămân cum erau, iar generatorul cere
    /// valorile lipsă abia la următoarea generare a contractului.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddRentalContractTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.company_profiles
                    ADD COLUMN IF NOT EXISTS legal_representative_role character varying(64) NULL;

                ALTER TABLE public.tenants
                    ADD COLUMN IF NOT EXISTS representative_name character varying(256) NULL;

                ALTER TABLE public.fleet_rental_defaults
                    ADD COLUMN IF NOT EXISTS payment_due_day character varying(16) NULL,
                    ADD COLUMN IF NOT EXISTS payment_method character varying(16) NULL,
                    ADD COLUMN IF NOT EXISTS late_penalty_percent_per_day numeric(5,2) NULL,
                    ADD COLUMN IF NOT EXISTS franchise_bani bigint NULL,
                    ADD COLUMN IF NOT EXISTS notice_days integer NULL,
                    ADD COLUMN IF NOT EXISTS unavailability_rule character varying(16) NULL;

                ALTER TABLE public.rentals
                    ADD COLUMN IF NOT EXISTS payment_due_day character varying(16) NULL,
                    ADD COLUMN IF NOT EXISTS payment_method character varying(16) NULL,
                    ADD COLUMN IF NOT EXISTS late_penalty_percent_per_day numeric(5,2) NULL,
                    ADD COLUMN IF NOT EXISTS franchise_bani bigint NULL,
                    ADD COLUMN IF NOT EXISTS notice_days integer NULL,
                    ADD COLUMN IF NOT EXISTS unavailability_rule character varying(16) NULL,
                    ADD COLUMN IF NOT EXISTS additional_driver_name character varying(256) NULL,
                    ADD COLUMN IF NOT EXISTS additional_driver_cnp character varying(32) NULL,
                    ADD COLUMN IF NOT EXISTS additional_driver_license character varying(32) NULL,
                    ADD COLUMN IF NOT EXISTS keys_count integer NULL,
                    ADD COLUMN IF NOT EXISTS handover_documents jsonb NOT NULL DEFAULT '[]'::jsonb,
                    ADD COLUMN IF NOT EXISTS handover_documents_other character varying(512) NULL,
                    ADD COLUMN IF NOT EXISTS damages_at_pickup character varying(2048) NULL;

                ALTER TABLE public.check_records
                    ADD COLUMN IF NOT EXISTS keys_count integer NULL,
                    ADD COLUMN IF NOT EXISTS documents jsonb NOT NULL DEFAULT '[]'::jsonb,
                    ADD COLUMN IF NOT EXISTS documents_other character varying(512) NULL,
                    ADD COLUMN IF NOT EXISTS damages character varying(2048) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public.check_records
                    DROP COLUMN IF EXISTS damages,
                    DROP COLUMN IF EXISTS documents_other,
                    DROP COLUMN IF EXISTS documents,
                    DROP COLUMN IF EXISTS keys_count;

                ALTER TABLE public.rentals
                    DROP COLUMN IF EXISTS damages_at_pickup,
                    DROP COLUMN IF EXISTS handover_documents_other,
                    DROP COLUMN IF EXISTS handover_documents,
                    DROP COLUMN IF EXISTS keys_count,
                    DROP COLUMN IF EXISTS additional_driver_license,
                    DROP COLUMN IF EXISTS additional_driver_cnp,
                    DROP COLUMN IF EXISTS additional_driver_name,
                    DROP COLUMN IF EXISTS unavailability_rule,
                    DROP COLUMN IF EXISTS notice_days,
                    DROP COLUMN IF EXISTS franchise_bani,
                    DROP COLUMN IF EXISTS late_penalty_percent_per_day,
                    DROP COLUMN IF EXISTS payment_method,
                    DROP COLUMN IF EXISTS payment_due_day;

                ALTER TABLE public.fleet_rental_defaults
                    DROP COLUMN IF EXISTS unavailability_rule,
                    DROP COLUMN IF EXISTS notice_days,
                    DROP COLUMN IF EXISTS franchise_bani,
                    DROP COLUMN IF EXISTS late_penalty_percent_per_day,
                    DROP COLUMN IF EXISTS payment_method,
                    DROP COLUMN IF EXISTS payment_due_day;

                ALTER TABLE public.tenants DROP COLUMN IF EXISTS representative_name;

                ALTER TABLE public.company_profiles DROP COLUMN IF EXISTS legal_representative_role;
                """);
        }
    }
}
