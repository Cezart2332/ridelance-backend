using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Documentele se pot lega de o mașină din flotă.
    ///
    /// Până acum un document putea aparține unui utilizator, unui dosar PFA sau unui vehicul din
    /// onboarding — dar nu unei mașini din marketplace. Așa că talonul unei mașini de flotă se
    /// încărca în teancul general al firmei, fără să se știe a cui e.
    ///
    /// Nimic de completat retroactiv: legătura n-a existat, deci nu se poate deduce.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCarIdToDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "car_id",
                schema: "public",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_car_id_uploaded_at_utc",
                schema: "public",
                table: "documents",
                columns: ["car_id", "uploaded_at_utc"],
                descending: [false, true],
                filter: "car_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_documents_car_id_uploaded_at_utc",
                schema: "public",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "car_id",
                schema: "public",
                table: "documents");
        }
    }
}
