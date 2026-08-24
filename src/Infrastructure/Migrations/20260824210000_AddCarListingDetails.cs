using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Câmpurile cerute de fluxul de adăugare a unei mașini, pe cei șase pași.
    ///
    /// Toate sunt nullable, inclusiv cele din dosarul vehiculului: principiul fluxului e că o
    /// mașină se publică fără talon, RCA sau VIN, iar dosarul se completează ulterior. O coloană
    /// obligatorie aici ar fi contrazis exact asta.
    ///
    /// <c>show_exact_location</c> pornește pe `false`: adresa unde stă mașina noaptea nu e o
    /// informație pe care proprietarul o dă fără să fie întrebat. <c>use_company_contacts</c>
    /// pornește pe `true`, fiindcă e comportamentul de dinaintea acestei migrații.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCarListingDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "zone", schema: "public", table: "cars", type: "character varying(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<double>(name: "latitude", schema: "public", table: "cars", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<double>(name: "longitude", schema: "public", table: "cars", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "show_exact_location", schema: "public", table: "cars", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "use_company_contacts", schema: "public", table: "cars", type: "boolean", nullable: false, defaultValue: true);

            migrationBuilder.AddColumn<string>(name: "color", schema: "public", table: "cars", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<int>(name: "seats", schema: "public", table: "cars", type: "integer", nullable: true);

            migrationBuilder.AddColumn<string>(name: "minimum_period", schema: "public", table: "cars", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "conditions", schema: "public", table: "cars", type: "character varying(1024)", maxLength: 1024, nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "available_from_utc", schema: "public", table: "cars", type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<string>(name: "plate_number", schema: "public", table: "cars", type: "character varying(16)", maxLength: 16, nullable: true);
            migrationBuilder.AddColumn<string>(name: "vin", schema: "public", table: "cars", type: "character varying(32)", maxLength: 32, nullable: true);
            migrationBuilder.AddColumn<int>(name: "mileage", schema: "public", table: "cars", type: "integer", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "first_registration_at_utc", schema: "public", table: "cars", type: "timestamp with time zone", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (string column in new[]
            {
                "zone", "latitude", "longitude", "show_exact_location", "use_company_contacts",
                "color", "seats", "minimum_period", "conditions", "available_from_utc",
                "plate_number", "vin", "mileage", "first_registration_at_utc",
            })
            {
                migrationBuilder.DropColumn(name: column, schema: "public", table: "cars");
            }
        }
    }
}
