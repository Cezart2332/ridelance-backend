using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Cheltuiala capătă datele pe care le citește OCR-ul de pe bon sau factură: data actului,
    /// furnizorul, TVA-ul, moneda, tipul documentului, sursa și extragerea brută.
    ///
    /// Cele două valori implicite spun ce era adevărat despre rândurile existente, nu ce ne-ar
    /// conveni: tot ce s-a introdus până acum a fost completat de om (`Manual`) și a fost tratat
    /// ca definitiv (`Confirmed`). A le marca drept `Draft` ar scoate retroactiv din profit
    /// cheltuieli pe care utilizatorii le consideră de mult înregistrate.
    ///
    /// `updated_at_utc` primește data creării — singurul moment în care rândul a fost atins.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddExpenseOcrFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "expense_date",
                schema: "public",
                table: "deductible_expenses",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplier_name",
                schema: "public",
                table: "deductible_expenses",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "vat_amount",
                schema: "public",
                table: "deductible_expenses",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "public",
                table: "deductible_expenses",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RON");

            migrationBuilder.AddColumn<string>(
                name: "document_type_label",
                schema: "public",
                table: "deductible_expenses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "public",
                table: "deductible_expenses",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "public",
                table: "deductible_expenses",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Confirmed");

            migrationBuilder.AddColumn<string>(
                name: "extraction_json",
                schema: "public",
                table: "deductible_expenses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at_utc",
                schema: "public",
                table: "deductible_expenses",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.Sql("""
                UPDATE public.deductible_expenses
                SET updated_at_utc = created_at_utc;
                """);

            // Profitul se calculează pe cheltuielile confirmate ale unui PFA într-o lună.
            migrationBuilder.CreateIndex(
                name: "ix_deductible_expenses_pfa_registration_id_status",
                schema: "public",
                table: "deductible_expenses",
                columns: ["pfa_registration_id", "status"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_deductible_expenses_pfa_registration_id_status",
                schema: "public",
                table: "deductible_expenses");

            migrationBuilder.DropColumn(name: "updated_at_utc", schema: "public", table: "deductible_expenses");
            migrationBuilder.DropColumn(name: "extraction_json", schema: "public", table: "deductible_expenses");
            migrationBuilder.DropColumn(name: "status", schema: "public", table: "deductible_expenses");
            migrationBuilder.DropColumn(name: "source", schema: "public", table: "deductible_expenses");
            migrationBuilder.DropColumn(name: "document_type_label", schema: "public", table: "deductible_expenses");
            migrationBuilder.DropColumn(name: "currency", schema: "public", table: "deductible_expenses");
            migrationBuilder.DropColumn(name: "vat_amount", schema: "public", table: "deductible_expenses");
            migrationBuilder.DropColumn(name: "supplier_name", schema: "public", table: "deductible_expenses");
            migrationBuilder.DropColumn(name: "expense_date", schema: "public", table: "deductible_expenses");
        }
    }
}
