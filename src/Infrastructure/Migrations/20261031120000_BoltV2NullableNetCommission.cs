using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Bolt Fleet API 2.0 nu mai trimite netul și comisionul pe cursă: cursele noi le au goale.
    /// Cursele aduse prin V1 își păstrează valorile.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class BoltV2NullableNetCommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "net_earnings",
                schema: "public",
                table: "bolt_orders",
                type: "numeric(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "commission",
                schema: "public",
                table: "bolt_orders",
                type: "numeric(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE public.bolt_orders SET net_earnings = 0 WHERE net_earnings IS NULL;");
            migrationBuilder.Sql("UPDATE public.bolt_orders SET commission = 0 WHERE commission IS NULL;");

            migrationBuilder.AlterColumn<decimal>(
                name: "net_earnings",
                schema: "public",
                table: "bolt_orders",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "commission",
                schema: "public",
                table: "bolt_orders",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldNullable: true);
        }
    }
}
