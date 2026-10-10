using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Blurarea numărului de înmatriculare devine opțiunea plătită, nu ceva făcut la orice poză: pe
    /// fiecare poză se notează când a fost blurată. Pozele existente au trecut toate prin blurarea
    /// de la încărcare, deci primesc data încărcării.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class CarImagePlateBlur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "plate_blurred_at_utc",
                schema: "public",
                table: "car_images",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE public.car_images SET plate_blurred_at_utc = uploaded_at_utc;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "plate_blurred_at_utc", schema: "public", table: "car_images");
        }
    }
}
