using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Plățile încasate de la chiriaș, înregistrate de proprietar.
    ///
    /// Se **înregistrează**, nu se încasează prin platformă: banii trec direct între flotă și
    /// chiriaș. De aceea nu există status de plată și nici legătură cu Stripe — ar sugera o
    /// încasare pe care n-o facem noi.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddRentalPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rental_payments",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rental_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_bani = table.Column<long>(type: "bigint", nullable: false),
                    paid_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    notes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rental_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_rental_payments_rentals_rental_id",
                        column: x => x.rental_id,
                        principalSchema: "public",
                        principalTable: "rentals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rental_payments_rental_id_paid_on_utc",
                schema: "public",
                table: "rental_payments",
                columns: ["rental_id", "paid_on_utc"],
                descending: [false, true]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "rental_payments", schema: "public");
        }
    }
}
