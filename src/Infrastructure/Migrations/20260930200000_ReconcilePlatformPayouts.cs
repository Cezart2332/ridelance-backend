using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Spec flux contabil R20–R22 și R40–R43, pe datele existente.
///
/// 1. IBAN-ul contrapartidei pe tranzacțiile bancare, recuperat din răspunsul băncii
///    (<c>creditorAccount</c> la plăți, <c>debtorAccount</c> la încasări). Recunoaște transferurile
///    între conturile PFA și cele către titular.
/// 2. Payout-urile Uber/Bolt importate ca venit (modul NetPayout, scos) sau ca transfer (GrossReport)
///    devin decontări nereconciliate: nu mai sunt venit până la reconcilierea cu raportul, care rulează
///    la următorul import. Cele din luni închise rămân cum sunt; se corectează prin corecție.
/// 3. Plățile bancare fără document justificativ: <c>Unmatched</c> (R01, R02).
/// </summary>
public partial class ReconcilePlatformPayouts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "counterparty_iban",
            schema: "public",
            table: "bank_transactions",
            type: "character varying(34)",
            maxLength: 34,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE public.bank_transactions
            SET counterparty_iban = upper(replace(
                CASE WHEN amount < 0 THEN raw_json -> 'creditorAccount' ->> 'iban'
                     ELSE raw_json -> 'debtorAccount' ->> 'iban' END, ' ', ''))
            WHERE raw_json IS NOT NULL AND jsonb_typeof(raw_json) = 'object';
            """);

        migrationBuilder.Sql(
            """
            UPDATE public.ledger_entries
            SET transaction_type = 'PlatformSettlement',
                reconciliation_status = 'NeedsReconciliation',
                platform_document_id = NULL
            WHERE source IN ('Bolt', 'Uber')
              AND bank_transaction_id IS NOT NULL
              AND amount > 0
              AND transaction_type IN ('Income', 'Transfer')
              AND status <> 'Locked'
              AND settlement_group_id IS NULL;
            """);

        migrationBuilder.Sql(
            """
            UPDATE public.ledger_entries e
            SET reconciliation_status = 'Unmatched'
            WHERE e.source = 'Bank'
              AND e.transaction_type = 'Expense'
              AND e.reconciliation_status = 'Matched'
              AND e.source_document_id IS NULL
              AND NOT EXISTS (SELECT 1 FROM public.expense_documents d WHERE d.ledger_entry_id = e.id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "counterparty_iban", schema: "public", table: "bank_transactions");
    }
}
