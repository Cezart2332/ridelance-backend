using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class AddDeductibleExpenses : Migration
{
    private static readonly string[] PfaYearMonthIndexColumns = ["pfa_registration_id", "year", "month"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "deductible_expenses",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                document_id = table.Column<Guid>(type: "uuid", nullable: false),
                catalog_category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                item_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                deductible_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                amount_ron = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                year = table.Column<int>(type: "integer", nullable: false),
                month = table.Column<int>(type: "integer", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_deductible_expenses", x => x.id);
                table.ForeignKey(
                    name: "fk_deductible_expenses_documents_document_id",
                    column: x => x.document_id,
                    principalSchema: "public",
                    principalTable: "documents",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_deductible_expenses_pfa_registrations_pfa_registration_id",
                    column: x => x.pfa_registration_id,
                    principalSchema: "public",
                    principalTable: "pfa_registrations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_deductible_expenses_users_created_by_user_id",
                    column: x => x.created_by_user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_deductible_expenses_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_deductible_expenses_created_by_user_id",
            schema: "public",
            table: "deductible_expenses",
            column: "created_by_user_id");

        migrationBuilder.CreateIndex(
            name: "ix_deductible_expenses_document_id",
            schema: "public",
            table: "deductible_expenses",
            column: "document_id");

        migrationBuilder.CreateIndex(
            name: "ix_deductible_expenses_pfa_registration_id_year_month",
            schema: "public",
            table: "deductible_expenses",
            columns: PfaYearMonthIndexColumns);

        migrationBuilder.CreateIndex(
            name: "ix_deductible_expenses_user_id",
            schema: "public",
            table: "deductible_expenses",
            column: "user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "deductible_expenses",
            schema: "public");
    }
}
