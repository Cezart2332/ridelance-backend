using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Facturile primite prin e-Factura primesc starea de plată (spec flux contabil R03–R04b): toate pornesc
/// neplătite; plata se leagă din bancă la următorul import al ledger-ului.
/// </summary>
public partial class AddEFacturaPaymentStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "paid_amount",
            schema: "public",
            table: "efactura_messages",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "payment_status",
            schema: "public",
            table: "efactura_messages",
            type: "character varying(48)",
            maxLength: 48,
            nullable: false,
            defaultValue: "Unpaid");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "paid_amount", schema: "public", table: "efactura_messages");
        migrationBuilder.DropColumn(name: "payment_status", schema: "public", table: "efactura_messages");
    }
}
