using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Ledger-ul primește ce îi lipsea față de spec-ul fluxului contabil (§4): starea de reconciliere,
/// grupul de decontare (venit brut + comision pe același payout), legătura cu factura e-Factura,
/// data documentului și partea personală a sumei.
///
/// Rândurile existente pornesc <c>Matched</c>, cu excepția celor deja „de verificat”. Payout-urile
/// platformelor se reclasifică la pasul următor (R20), nu aici.
/// </summary>
public partial class ExtendLedgerForAccountingFlow : Migration
{
    private const string Schema = "public";
    private const string Table = "ledger_entries";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "reconciliation_status",
            schema: Schema,
            table: Table,
            type: "character varying(48)",
            maxLength: 48,
            nullable: false,
            defaultValue: "Matched");

        migrationBuilder.AddColumn<Guid>(
            name: "settlement_group_id",
            schema: Schema,
            table: Table,
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "e_factura_message_id",
            schema: Schema,
            table: Table,
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "document_date",
            schema: Schema,
            table: Table,
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "personal_amount",
            schema: Schema,
            table: Table,
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.Sql($"UPDATE {Schema}.{Table} SET reconciliation_status = 'NeedsReview' WHERE status = 'NeedsReview';");

        migrationBuilder.CreateIndex(
            name: "ix_ledger_entries_settlement_group_id",
            schema: Schema,
            table: Table,
            column: "settlement_group_id",
            filter: "settlement_group_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_ledger_entries_e_factura_message_id",
            schema: Schema,
            table: Table,
            column: "e_factura_message_id");

        migrationBuilder.AddForeignKey(
            name: "fk_ledger_entries_efactura_messages_e_factura_message_id",
            schema: Schema,
            table: Table,
            column: "e_factura_message_id",
            principalSchema: Schema,
            principalTable: "efactura_messages",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "fk_ledger_entries_efactura_messages_e_factura_message_id", schema: Schema, table: Table);
        migrationBuilder.DropIndex(name: "ix_ledger_entries_e_factura_message_id", schema: Schema, table: Table);
        migrationBuilder.DropIndex(name: "ix_ledger_entries_settlement_group_id", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "reconciliation_status", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "settlement_group_id", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "e_factura_message_id", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "document_date", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "personal_amount", schema: Schema, table: Table);
    }
}
