using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Registrele obligatorii PFA (spec registre §4–§7): venitul impozabil și decizia de mijloc fix pe
/// înregistrări, activele cu număr de inventar și plan de amortizare, pragul de mijloc fix,
/// inventarierea, anul contabil și explicațiile controalelor lunii. Activele existente (test)
/// primesc numere MF-0001… și rămân de clasificat: nu au clasă, durată și punere în funcțiune.
/// </summary>
public partial class AddRegistersModel : Migration
{
    private const string Schema = "public";

    private static readonly string[] AssetNumber = ["pfa_registration_id", "inventory_number"];
    private static readonly string[] LineKey = ["asset_id", "year", "month"];
    private static readonly string[] LinePeriod = ["pfa_registration_id", "year", "month"];
    private static readonly string[] CountKey = ["pfa_registration_id", "date", "reason"];
    private static readonly string[] YearKey = ["pfa_registration_id", "year"];
    private static readonly string[] ExplanationKey = ["pfa_registration_id", "period", "control"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        LedgerEntries(migrationBuilder);
        Assets(migrationBuilder);

        migrationBuilder.AddColumn<decimal>(name: "balance", schema: Schema, table: "bank_accounts", type: "numeric(18,2)", precision: 18, scale: 2, nullable: true);
        migrationBuilder.AddColumn<DateOnly>(name: "balance_date", schema: Schema, table: "bank_accounts", type: "date", nullable: true);

        migrationBuilder.CreateTable(
            name: "fixed_asset_rules",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                threshold = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                depreciation_start = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                excluded_categories = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                valid_to = table.Column<DateOnly>(type: "date", nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_fixed_asset_rules", x => x.id));
        migrationBuilder.CreateIndex(name: "ix_fixed_asset_rules_valid_from", schema: Schema, table: "fixed_asset_rules", column: "valid_from", unique: true);
        // Q3: pragul de 2.500 lei (Codul fiscal, art. 7 pct. 24) și amortizarea din luna următoare.
        migrationBuilder.InsertData(
            schema: Schema,
            table: "fixed_asset_rules",
            columnTypes: ["uuid", "numeric(18,2)", "character varying(48)", "character varying(1000)", "date", "date"],
            columns: ["id", "threshold", "depreciation_start", "excluded_categories", "valid_from", "valid_to"],
            values: new object[] { new Guid("b1c2d3e4-f5a6-4b7c-8d9e-0000000fa001"), 2500m, "NextMonth", "FUEL|CAR_SERVICE|CAR_INSURANCE|CAR_WASH|PHONE|PERSONAL|DEPRECIATION|PLATFORM_COMMISSION", new DateOnly(2016, 1, 1), null });

        migrationBuilder.CreateTable(
            name: "depreciation_lines",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                year = table.Column<int>(type: "integer", nullable: false),
                month = table.Column<int>(type: "integer", nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                accumulated = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                remaining = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                is_locked = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_depreciation_lines", x => x.id);
                table.ForeignKey("fk_depreciation_lines_pfa_assets_asset_id", x => x.asset_id, "pfa_assets", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_depreciation_lines_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, "pfa_registrations", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_depreciation_lines_asset_id_year_month", schema: Schema, table: "depreciation_lines", columns: LineKey, unique: true);
        migrationBuilder.CreateIndex(name: "ix_depreciation_lines_pfa_registration_id_year_month", schema: Schema, table: "depreciation_lines", columns: LinePeriod);

        migrationBuilder.CreateTable(
            name: "inventory_counts",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                date = table.Column<DateOnly>(type: "date", nullable: false),
                reason = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                finalized_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                finalized_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                snapshot_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_inventory_counts", x => x.id);
                table.ForeignKey("fk_inventory_counts_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, "pfa_registrations", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_inventory_counts_documents_snapshot_document_id", x => x.snapshot_document_id, "documents", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_inventory_counts_users_finalized_by_user_id", x => x.finalized_by_user_id, "users", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_inventory_counts_pfa_registration_id_date_reason", schema: Schema, table: "inventory_counts", columns: CountKey, unique: true);
        migrationBuilder.CreateIndex(name: "ix_inventory_counts_snapshot_document_id", schema: Schema, table: "inventory_counts", column: "snapshot_document_id");
        migrationBuilder.CreateIndex(name: "ix_inventory_counts_finalized_by_user_id", schema: Schema, table: "inventory_counts", column: "finalized_by_user_id");

        migrationBuilder.CreateTable(
            name: "inventory_items",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                inventory_count_id = table.Column<Guid>(type: "uuid", nullable: false),
                category = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                system_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                confirmed_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                source_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                source_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                requires_confirmation = table.Column<bool>(type: "boolean", nullable: false),
                note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_inventory_items", x => x.id);
                table.ForeignKey("fk_inventory_items_inventory_counts_inventory_count_id", x => x.inventory_count_id, "inventory_counts", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_inventory_items_inventory_count_id", schema: Schema, table: "inventory_items", column: "inventory_count_id");

        migrationBuilder.CreateTable(
            name: "accounting_years",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                year = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ref_json = table.Column<string>(type: "jsonb", nullable: true),
                package_document_id = table.Column<Guid>(type: "uuid", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_accounting_years", x => x.id);
                table.ForeignKey("fk_accounting_years_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, "pfa_registrations", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_accounting_years_documents_package_document_id", x => x.package_document_id, "documents", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_accounting_years_users_closed_by_user_id", x => x.closed_by_user_id, "users", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_accounting_years_pfa_registration_id_year", schema: Schema, table: "accounting_years", columns: YearKey, unique: true);
        migrationBuilder.CreateIndex(name: "ix_accounting_years_package_document_id", schema: Schema, table: "accounting_years", column: "package_document_id");
        migrationBuilder.CreateIndex(name: "ix_accounting_years_closed_by_user_id", schema: Schema, table: "accounting_years", column: "closed_by_user_id");

        migrationBuilder.CreateTable(
            name: "reconciliation_explanations",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                control = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_reconciliation_explanations", x => x.id);
                table.ForeignKey("fk_reconciliation_explanations_pfa_registrations_pfa_registration_id", x => x.pfa_registration_id, "pfa_registrations", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_reconciliation_explanations_users_created_by_user_id", x => x.created_by_user_id, "users", "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_reconciliation_explanations_pfa_registration_id_period_control", schema: Schema, table: "reconciliation_explanations", columns: ExplanationKey);
        migrationBuilder.CreateIndex(name: "ix_reconciliation_explanations_created_by_user_id", schema: Schema, table: "reconciliation_explanations", column: "created_by_user_id");
    }

    private static void LedgerEntries(MigrationBuilder migrationBuilder)
    {
        const string Table = "ledger_entries";
        migrationBuilder.AddColumn<decimal>(name: "taxable_income_amount", schema: Schema, table: Table, type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<string>(name: "income_source", schema: Schema, table: Table, type: "character varying(48)", maxLength: 48, nullable: false, defaultValue: "Ridesharing");
        migrationBuilder.AddColumn<string>(name: "fixed_asset_review", schema: Schema, table: Table, type: "character varying(48)", maxLength: 48, nullable: false, defaultValue: "None");

        // Tax Engine pe ce există: venitul impozabil e încasarea efectivă (LedgerEntry.RefreshTaxableIncome).
        migrationBuilder.Sql($"""
            UPDATE {Schema}.{Table} SET taxable_income_amount = amount
            WHERE transaction_type = 'Income'
              AND reconciliation_status <> 'NeedsReconciliation'
              AND NOT (bank_transaction_id IS NULL AND platform_document_id IS NOT NULL)
              AND NOT closed_period_flag;
            """);
    }

    private static void Assets(MigrationBuilder migrationBuilder)
    {
        const string Table = "pfa_assets";
        migrationBuilder.DropIndex(name: "ix_pfa_assets_pfa_registration_id", schema: Schema, table: Table);
        migrationBuilder.RenameColumn(name: "description", schema: Schema, table: Table, newName: "name");
        migrationBuilder.RenameColumn(name: "acquisition_date", schema: Schema, table: Table, newName: "entry_date");
        migrationBuilder.RenameColumn(name: "acquisition_value", schema: Schema, table: Table, newName: "entry_value");
        migrationBuilder.RenameColumn(name: "disposed_date", schema: Schema, table: Table, newName: "disposal_date");

        migrationBuilder.AddColumn<string>(name: "inventory_number", schema: Schema, table: Table, type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "kind", schema: Schema, table: Table, type: "character varying(48)", maxLength: 48, nullable: false, defaultValue: "FixedAsset");
        migrationBuilder.AddColumn<Guid>(name: "acquisition_entry_id", schema: Schema, table: Table, type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "document_ref", schema: Schema, table: Table, type: "character varying(256)", maxLength: 256, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "supplier_name", schema: Schema, table: Table, type: "character varying(256)", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<DateOnly>(name: "in_service_date", schema: Schema, table: Table, type: "date", nullable: true);
        migrationBuilder.AddColumn<string>(name: "depreciation_class_code", schema: Schema, table: Table, type: "character varying(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<int>(name: "normal_life_months", schema: Schema, table: Table, type: "integer", nullable: true);
        migrationBuilder.AddColumn<string>(name: "method", schema: Schema, table: Table, type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Linear");
        migrationBuilder.AddColumn<string>(name: "disposal_reason", schema: Schema, table: Table, type: "character varying(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "created_by_user_id", schema: Schema, table: Table, type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "created_at_utc", schema: Schema, table: Table, type: "timestamp with time zone", nullable: false, defaultValueSql: "now()");

        migrationBuilder.Sql($"""
            UPDATE {Schema}.{Table} a SET
                name = left(a.type || ' — ' || a.name, 500),
                inventory_number = 'MF-' || lpad(n.rn::text, 4, '0'),
                status = CASE WHEN a.status = 'Disposed' THEN 'Disposed' ELSE 'PendingClassification' END
            FROM (SELECT id, row_number() OVER (PARTITION BY pfa_registration_id ORDER BY entry_date, id) AS rn FROM {Schema}.{Table}) n
            WHERE n.id = a.id;
            """);
        migrationBuilder.DropColumn(name: "type", schema: Schema, table: Table);

        migrationBuilder.CreateIndex(name: "ix_pfa_assets_pfa_registration_id_inventory_number", schema: Schema, table: Table, columns: AssetNumber, unique: true);
        migrationBuilder.CreateIndex(name: "ix_pfa_assets_acquisition_entry_id", schema: Schema, table: Table, column: "acquisition_entry_id", unique: true, filter: "acquisition_entry_id IS NOT NULL");
        migrationBuilder.CreateIndex(name: "ix_pfa_assets_created_by_user_id", schema: Schema, table: Table, column: "created_by_user_id");
        migrationBuilder.AddForeignKey(name: "fk_pfa_assets_ledger_entries_acquisition_entry_id", schema: Schema, table: Table, column: "acquisition_entry_id", principalSchema: Schema, principalTable: "ledger_entries", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "fk_pfa_assets_users_created_by_user_id", schema: Schema, table: Table, column: "created_by_user_id", principalSchema: Schema, principalTable: "users", principalColumn: "id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "reconciliation_explanations", schema: Schema);
        migrationBuilder.DropTable(name: "accounting_years", schema: Schema);
        migrationBuilder.DropTable(name: "inventory_items", schema: Schema);
        migrationBuilder.DropTable(name: "inventory_counts", schema: Schema);
        migrationBuilder.DropTable(name: "depreciation_lines", schema: Schema);
        migrationBuilder.DropTable(name: "fixed_asset_rules", schema: Schema);
        migrationBuilder.DropColumn(name: "balance", schema: Schema, table: "bank_accounts");
        migrationBuilder.DropColumn(name: "balance_date", schema: Schema, table: "bank_accounts");
        migrationBuilder.DropColumn(name: "taxable_income_amount", schema: Schema, table: "ledger_entries");
        migrationBuilder.DropColumn(name: "income_source", schema: Schema, table: "ledger_entries");
        migrationBuilder.DropColumn(name: "fixed_asset_review", schema: Schema, table: "ledger_entries");

        const string Table = "pfa_assets";
        migrationBuilder.DropForeignKey(name: "fk_pfa_assets_ledger_entries_acquisition_entry_id", schema: Schema, table: Table);
        migrationBuilder.DropForeignKey(name: "fk_pfa_assets_users_created_by_user_id", schema: Schema, table: Table);
        migrationBuilder.DropIndex(name: "ix_pfa_assets_pfa_registration_id_inventory_number", schema: Schema, table: Table);
        migrationBuilder.DropIndex(name: "ix_pfa_assets_acquisition_entry_id", schema: Schema, table: Table);
        migrationBuilder.DropIndex(name: "ix_pfa_assets_created_by_user_id", schema: Schema, table: Table);
        migrationBuilder.AddColumn<string>(name: "type", schema: Schema, table: Table, type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "Activ");
        migrationBuilder.Sql($"UPDATE {Schema}.{Table} SET status = 'InUse' WHERE status <> 'Disposed';");
        foreach (string column in (string[])["inventory_number", "kind", "acquisition_entry_id", "document_ref", "supplier_name", "in_service_date", "depreciation_class_code", "normal_life_months", "method", "disposal_reason", "created_by_user_id", "created_at_utc"])
        {
            migrationBuilder.DropColumn(name: column, schema: Schema, table: Table);
        }

        migrationBuilder.RenameColumn(name: "name", schema: Schema, table: Table, newName: "description");
        migrationBuilder.RenameColumn(name: "entry_date", schema: Schema, table: Table, newName: "acquisition_date");
        migrationBuilder.RenameColumn(name: "entry_value", schema: Schema, table: Table, newName: "acquisition_value");
        migrationBuilder.RenameColumn(name: "disposal_date", schema: Schema, table: Table, newName: "disposed_date");
        migrationBuilder.CreateIndex(name: "ix_pfa_assets_pfa_registration_id", schema: Schema, table: Table, column: "pfa_registration_id");
    }
}
