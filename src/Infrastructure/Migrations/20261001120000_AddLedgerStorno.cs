using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Corecțiile lunilor închise prin stornare (spec flux contabil §4): înregistrarea blocată rămâne
/// neatinsă, iar în luna curentă intră stornarea ei și, dacă e cazul, înregistrarea corectată.
/// </summary>
public partial class AddLedgerStorno : Migration
{
    private const string Schema = "public";
    private const string Table = "ledger_entries";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "storno_of_entry_id", schema: Schema, table: Table, type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "corrects_entry_id", schema: Schema, table: Table, type: "uuid", nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_ledger_entries_storno_of_entry_id",
            schema: Schema,
            table: Table,
            column: "storno_of_entry_id",
            unique: true,
            filter: "storno_of_entry_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_ledger_entries_corrects_entry_id",
            schema: Schema,
            table: Table,
            column: "corrects_entry_id");

        migrationBuilder.AddForeignKey(
            name: "fk_ledger_entries_ledger_entries_storno_of_entry_id",
            schema: Schema,
            table: Table,
            column: "storno_of_entry_id",
            principalSchema: Schema,
            principalTable: Table,
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_ledger_entries_ledger_entries_corrects_entry_id",
            schema: Schema,
            table: Table,
            column: "corrects_entry_id",
            principalSchema: Schema,
            principalTable: Table,
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "fk_ledger_entries_ledger_entries_storno_of_entry_id", schema: Schema, table: Table);
        migrationBuilder.DropForeignKey(name: "fk_ledger_entries_ledger_entries_corrects_entry_id", schema: Schema, table: Table);
        migrationBuilder.DropIndex(name: "ix_ledger_entries_storno_of_entry_id", schema: Schema, table: Table);
        migrationBuilder.DropIndex(name: "ix_ledger_entries_corrects_entry_id", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "storno_of_entry_id", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "corrects_entry_id", schema: Schema, table: Table);
    }
}
