using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Registrul de furnizori: ștergere logică (un furnizor adăugat greșit și nefolosit în declarații
    /// dispare din listă, verificări și calcul; rândul rămâne pentru audit).
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddSupplierSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                schema: "public",
                table: "supplier_tax_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_user_id",
                schema: "public",
                table: "supplier_tax_profiles",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "deleted_at_utc", schema: "public", table: "supplier_tax_profiles");
            migrationBuilder.DropColumn(name: "deleted_by_user_id", schema: "public", table: "supplier_tax_profiles");
        }
    }
}
