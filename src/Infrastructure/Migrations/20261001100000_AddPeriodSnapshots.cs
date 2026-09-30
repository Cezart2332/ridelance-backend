using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Registrele unei luni la închidere (spec flux contabil §7, §8): RJIP-ul lunii și REF-ul anului până la
/// sfârșitul ei, ca date și PDF. Redeschiderea nu le șterge; o nouă închidere adaugă altele.
/// </summary>
public partial class AddPeriodSnapshots : Migration
{
    private static readonly string[] PfaPeriod = ["pfa_registration_id", "period"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "accounting_period_snapshots",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                rjip_json = table.Column<string>(type: "jsonb", nullable: false),
                ref_json = table.Column<string>(type: "jsonb", nullable: false),
                rjip_pdf_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_accounting_period_snapshots", x => x.id);
                table.ForeignKey(
                    name: "fk_accounting_period_snapshots_pfa_registrations_pfa_registration_id",
                    column: x => x.pfa_registration_id,
                    principalSchema: "public",
                    principalTable: "pfa_registrations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_accounting_period_snapshots_documents_rjip_pdf_document_id",
                    column: x => x.rjip_pdf_document_id,
                    principalSchema: "public",
                    principalTable: "documents",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_accounting_period_snapshots_users_created_by_user_id",
                    column: x => x.created_by_user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "ix_accounting_period_snapshots_pfa_registration_id_period", schema: "public", table: "accounting_period_snapshots", columns: PfaPeriod);
        migrationBuilder.CreateIndex(name: "ix_accounting_period_snapshots_rjip_pdf_document_id", schema: "public", table: "accounting_period_snapshots", column: "rjip_pdf_document_id");
        migrationBuilder.CreateIndex(name: "ix_accounting_period_snapshots_created_by_user_id", schema: "public", table: "accounting_period_snapshots", column: "created_by_user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "accounting_period_snapshots", schema: "public");
    }
}
