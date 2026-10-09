using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Planurile noi: PFAlone (139 lei) și PFA Full (299 lei) în locul lui Solo / Start / Pro.
    /// Solo era fără contabilitate, deci devine PFAlone; Start și Pro o includeau, deci devin PFA
    /// Full. <c>pending_plan</c> e număr: 2 (Pro) nu mai există și trece pe 1 (PFA Full).
    ///
    /// Plus opțiunile plătite ale PFAlone (Open Banking, casă de marcat), pe abonament.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class PfaAloneAndPfaFullPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_open_banking_addon",
                schema: "public",
                table: "user_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_cash_register_addon",
                schema: "public",
                table: "user_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE public.user_subscriptions SET plan = 'PfaAlone' WHERE plan = 'Solo';
                UPDATE public.user_subscriptions SET plan = 'PfaFull' WHERE plan IN ('Start', 'Pro');
                UPDATE public.user_subscriptions SET pending_plan = 1 WHERE pending_plan = 2;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE public.user_subscriptions SET plan = 'Solo' WHERE plan = 'PfaAlone';
                UPDATE public.user_subscriptions SET plan = 'Start' WHERE plan = 'PfaFull';
                """);

            migrationBuilder.DropColumn(name: "has_open_banking_addon", schema: "public", table: "user_subscriptions");
            migrationBuilder.DropColumn(name: "has_cash_register_addon", schema: "public", table: "user_subscriptions");
        }
    }
}
