using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Luna gratuită de Open Banking pentru PFAlone: până când merge fără plată și când a refuzat.
    /// Abonamentele PFAlone existente, fără opțiunea plătită, primesc luna de acum.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class OpenBankingTrial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "open_banking_trial_ends_at_utc",
                schema: "public",
                table: "user_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "open_banking_declined_at_utc",
                schema: "public",
                table: "user_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE public.user_subscriptions
                SET open_banking_trial_ends_at_utc = NOW() + INTERVAL '1 month'
                WHERE plan = 'PfaAlone' AND NOT has_open_banking_addon;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "open_banking_trial_ends_at_utc", schema: "public", table: "user_subscriptions");
            migrationBuilder.DropColumn(name: "open_banking_declined_at_utc", schema: "public", table: "user_subscriptions");
        }
    }
}
