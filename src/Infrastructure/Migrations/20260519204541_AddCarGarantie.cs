using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddCarGarantie : Migration
{
    private static readonly string[] PfaRegistrationYearMonthColumns = ["pfa_registration_id", "year", "month"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "garantie",
            schema: "public",
            table: "cars",
            type: "decimal(10,2)",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "pfa_monthly_incomes",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                year = table.Column<int>(type: "integer", nullable: false),
                month = table.Column<int>(type: "integer", nullable: false),
                venit_cash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                venit_card = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                venit_bolt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                venit_uber = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                taxe_estimate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pfa_monthly_incomes", x => x.id);
                table.ForeignKey(
                    name: "fk_pfa_monthly_incomes_pfa_registrations_pfa_registration_id",
                    column: x => x.pfa_registration_id,
                    principalSchema: "public",
                    principalTable: "pfa_registrations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_pfa_monthly_incomes_pfa_registration_id_year_month",
            schema: "public",
            table: "pfa_monthly_incomes",
            columns: PfaRegistrationYearMonthColumns,
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "pfa_monthly_incomes",
            schema: "public");

        migrationBuilder.DropColumn(
            name: "garantie",
            schema: "public",
            table: "cars");
    }
}
