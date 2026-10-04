using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

public partial class ConnectFiscalLinkAccounting : Migration
{
    private static readonly string[] ZKey = ["pfa_registration_id", "register_serial", "z_number", "date"];
    private static readonly string[] OldZKey = ["pfa_registration_id", "z_number"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "last_sync_attempt_at_utc", schema: "public", table: "fiscal_link_clients", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "last_sync_at_utc", schema: "public", table: "fiscal_link_clients", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "last_sync_error", schema: "public", table: "fiscal_link_clients", type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.DropIndex(name: "ix_z_reports_pfa_registration_id_z_number", schema: "public", table: "z_reports");
        migrationBuilder.CreateIndex(name: "ix_z_reports_pfa_registration_id_register_serial_z_number_date", schema: "public", table: "z_reports", columns: ZKey, unique: true)
            .Annotation("Npgsql:NullsDistinct", false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_z_reports_pfa_registration_id_register_serial_z_number_date", schema: "public", table: "z_reports");
        migrationBuilder.CreateIndex(name: "ix_z_reports_pfa_registration_id_z_number", schema: "public", table: "z_reports", columns: OldZKey, unique: true);
        migrationBuilder.DropColumn(name: "last_sync_attempt_at_utc", schema: "public", table: "fiscal_link_clients");
        migrationBuilder.DropColumn(name: "last_sync_at_utc", schema: "public", table: "fiscal_link_clients");
        migrationBuilder.DropColumn(name: "last_sync_error", schema: "public", table: "fiscal_link_clients");
    }
}
