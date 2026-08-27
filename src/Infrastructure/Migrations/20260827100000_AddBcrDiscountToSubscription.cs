using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Reducerea BCR: 50 lei pe lună, șase luni, pentru cine își deschide cont prin RIDElance.
    ///
    /// Două date, nu un boolean: „a cerut" și „s-a confirmat" sunt momente diferite, iar între ele
    /// pot trece săptămâni. Un singur câmp ar fi obligat pe cineva să decidă ce înseamnă `true`.
    ///
    /// Ambele rămân goale pe rândurile existente — nimeni n-a putut cere reducerea până acum, deci
    /// nu există nimic de completat retroactiv.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddBcrDiscountToSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<System.DateTime>(
                name: "bcr_discount_requested_at_utc",
                schema: "public",
                table: "user_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "bcr_discount_confirmed_at_utc",
                schema: "public",
                table: "user_subscriptions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bcr_discount_requested_at_utc",
                schema: "public",
                table: "user_subscriptions");

            migrationBuilder.DropColumn(
                name: "bcr_discount_confirmed_at_utc",
                schema: "public",
                table: "user_subscriptions");
        }
    }
}
