using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Validarea actelor pentru dosar, ca decizie a echipei: clientul generează dosarul ARR sau al
    /// copiei conforme abia după ea. Null pe dosarele existente — adică încă nevalidate.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddDossierDocumentsValidatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "arr_dossier_documents_validated_at_utc",
                schema: "public",
                table: "pfa_registrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "vehicle_dossier_documents_validated_at_utc",
                schema: "public",
                table: "pfa_registrations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "arr_dossier_documents_validated_at_utc", schema: "public", table: "pfa_registrations");
            migrationBuilder.DropColumn(name: "vehicle_dossier_documents_validated_at_utc", schema: "public", table: "pfa_registrations");
        }
    }
}
