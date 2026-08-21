using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// <c>rentals</c> — închirierile mașinilor din flotă.
    ///
    /// Valorile contractuale (chirie, garanție, cost km extra) sunt copiate pe rând, nu citite din
    /// setările firmei: setările se schimbă, iar o închiriere semnată la 1.800 lei/săptămână
    /// trebuie să rămână la 1.800 și după ce tariful standard crește.
    ///
    /// <c>end_at_utc</c> și <c>closed_at_utc</c> coexistă intenționat — „până când era planificată"
    /// și „când s-a terminat efectiv" sunt întrebări diferite, iar suprascrierea primei ar fi șters
    /// ce s-a convenit inițial.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddRentals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rentals",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    car_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    tenant_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tenant_fiscal_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    tenant_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    tenant_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    start_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    weekly_rent_bani = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    deposit_bani = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    has_km_limit = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    extra_km_cost_bani = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    fuel_rule = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    start_mileage = table.Column<int>(type: "integer", nullable: true),
                    accessories = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rentals", x => x.id);
                    table.ForeignKey(
                        name: "fk_rentals_cars_car_id",
                        column: x => x.car_id,
                        principalSchema: "public",
                        principalTable: "cars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_rentals_owner_user_id_start_at_utc
                    ON public.rentals (owner_user_id, start_at_utc DESC);

                CREATE INDEX ix_rentals_car_id ON public.rentals (car_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "rentals", schema: "public");
        }
    }
}
