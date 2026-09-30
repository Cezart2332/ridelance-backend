using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Spec flux contabil R10–R12 și R24–R25. Bonurile emise de casa de marcat (fără încasări: doar
/// verifică totalul Z), raportul Z cu seria casei și totalurile numerar / card, iar raportul platformei
/// cu venitul încasat numerar, pentru controlul față de rapoartele Z ale perioadei.
/// </summary>
public partial class AddFiscalReceiptsAndCashControls : Migration
{
    private static readonly string[] ReceiptKey = ["pfa_registration_id", "register_serial", "number", "date"];
    private static readonly string[] ReceiptDate = ["pfa_registration_id", "date"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "cash_amount", schema: "public", table: "document_extractions", type: "numeric(18,2)", precision: 18, scale: 2, nullable: true);
        migrationBuilder.AddColumn<string>(name: "register_serial", schema: "public", table: "z_reports", type: "character varying(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "total_cash", schema: "public", table: "z_reports", type: "numeric(18,2)", precision: 18, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "total_card", schema: "public", table: "z_reports", type: "numeric(18,2)", precision: 18, scale: 2, nullable: true);

        migrationBuilder.CreateTable(
            name: "fiscal_receipts",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                register_serial = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                date = table.Column<DateOnly>(type: "date", nullable: false),
                issued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                external_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                z_report_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_fiscal_receipts", x => x.id);
                table.ForeignKey(
                    name: "fk_fiscal_receipts_pfa_registrations_pfa_registration_id",
                    column: x => x.pfa_registration_id,
                    principalSchema: "public",
                    principalTable: "pfa_registrations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_fiscal_receipts_z_reports_z_report_id",
                    column: x => x.z_report_id,
                    principalSchema: "public",
                    principalTable: "z_reports",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_fiscal_receipts_pfa_registration_id_register_serial_number_date",
            schema: "public",
            table: "fiscal_receipts",
            columns: ReceiptKey,
            unique: true);
        migrationBuilder.CreateIndex(name: "ix_fiscal_receipts_pfa_registration_id_date", schema: "public", table: "fiscal_receipts", columns: ReceiptDate);
        migrationBuilder.CreateIndex(name: "ix_fiscal_receipts_z_report_id", schema: "public", table: "fiscal_receipts", column: "z_report_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "fiscal_receipts", schema: "public");
        migrationBuilder.DropColumn(name: "cash_amount", schema: "public", table: "document_extractions");
        migrationBuilder.DropColumn(name: "register_serial", schema: "public", table: "z_reports");
        migrationBuilder.DropColumn(name: "total_cash", schema: "public", table: "z_reports");
        migrationBuilder.DropColumn(name: "total_card", schema: "public", table: "z_reports");
    }
}
