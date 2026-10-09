using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Pasul „ARR &amp; Cont Flotă” înlocuiește pașii ARR, Uber &amp; Bolt și Vehicul.
    ///
    /// - tabelele cererii și ale jurnalului de statusuri;
    /// - pe documente: numărul documentului oficial și marcajul „înlocuit”;
    /// - dosarele aflate pe pașii vechi primesc o cerere cu ce au ales deja (platformele, modul de
    ///   deținere a mașinii), ca nimic să nu se piardă; cele cu onboardingul încheiat intră direct
    ///   ca finalizate;
    /// - cererile ARR, de copie conformă, ecusoanele, conturile de trezorerie ARR și secțiunile vechi
    ///   dispar, odată cu pașii care le foloseau.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect. Nereversibilă: tabelele șterse nu se refac.
    /// </summary>
    public partial class ArrFleetStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "arr_fleet_applications",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    platforms = table.Column<int>(type: "integer", nullable: false),
                    payment_amount_bani = table.Column<long>(type: "bigint", nullable: false),
                    payment_amount_changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    vehicle_ownership = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reopened_reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    reopened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arr_fleet_applications", x => x.id);
                    table.ForeignKey(
                        name: "fk_arr_fleet_applications_pfa_registrations_pfa_registration_id",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arr_fleet_applications_pfa_registration_id",
                schema: "public",
                table: "arr_fleet_applications",
                column: "pfa_registration_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arr_fleet_applications_user_id",
                schema: "public",
                table: "arr_fleet_applications",
                column: "user_id");

            migrationBuilder.CreateTable(
                name: "arr_fleet_status_logs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arr_fleet_application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    to_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arr_fleet_status_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_arr_fleet_status_logs_arr_fleet_applications_arr_fleet_application_id",
                        column: x => x.arr_fleet_application_id,
                        principalSchema: "public",
                        principalTable: "arr_fleet_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arr_fleet_status_logs_arr_fleet_application_id",
                schema: "public",
                table: "arr_fleet_status_logs",
                column: "arr_fleet_application_id");

            migrationBuilder.AddColumn<string>(
                name: "document_number",
                schema: "public",
                table: "documents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_superseded",
                schema: "public",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Dosarele de pe pașii vechi: platformele alese și modul de deținere trec în cererea
            // nouă. Platformele sunt flag-uri (Uber = 1, Bolt = 2), suma e 400 lei + 8 lei pe
            // platformă. Cine terminase onboardingul rămâne terminat.
            migrationBuilder.Sql("""
                INSERT INTO public.arr_fleet_applications
                    (id, pfa_registration_id, user_id, status, platforms, payment_amount_bani,
                     vehicle_ownership, submitted_at_utc, created_at_utc, updated_at_utc)
                SELECT
                    gen_random_uuid(),
                    r.id,
                    r.user_id,
                    CASE WHEN r.onboarding_completed_at_utc IS NOT NULL THEN 'Completed' ELSE 'Draft' END,
                    p.flags,
                    CASE p.flags WHEN 0 THEN 0 WHEN 3 THEN 41600 ELSE 40800 END,
                    CASE v.ownership_mode
                        WHEN 'Owned' THEN 'Ownership'
                        WHEN 'Rented' THEN 'Rental'
                        WHEN 'Leased' THEN 'Leasing'
                        WHEN 'Comodat' THEN 'Loan'
                        ELSE NULL
                    END,
                    r.onboarding_completed_at_utc,
                    now(),
                    now()
                FROM public.pfa_registrations r
                CROSS JOIN LATERAL (
                    SELECT COALESCE(bit_or(CASE a.provider WHEN 'Uber' THEN 1 WHEN 'Bolt' THEN 2 ELSE 0 END), 0) AS flags
                    FROM public.pfa_platform_accounts a
                    WHERE a.pfa_registration_id = r.id AND a.is_selected_by_user
                ) p
                LEFT JOIN LATERAL (
                    SELECT pv.ownership_mode
                    FROM public.pfa_vehicles pv
                    WHERE pv.pfa_registration_id = r.id
                    ORDER BY pv.created_at_utc DESC
                    LIMIT 1
                ) v ON true
                WHERE p.flags <> 0
                   OR v.ownership_mode IS NOT NULL
                   OR r.onboarding_completed_at_utc IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                DELETE FROM public.onboarding_section_approvals
                WHERE section_key IN ('AutorizatieTransport', 'CopieConforma', 'Vehicul');
                """);

            migrationBuilder.DropTable(name: "vehicle_badges", schema: "public");
            migrationBuilder.DropTable(name: "vehicle_copy_requests", schema: "public");
            migrationBuilder.DropTable(name: "arr_authorization_requests", schema: "public");
            migrationBuilder.DropTable(name: "arr_accounts", schema: "public");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nereversibilă: cererile ARR, de copie conformă și ecusoanele au fost șterse odată cu
            // pașii vechi, iar datele lor nu se pot reface din cererea nouă.
        }
    }
}
