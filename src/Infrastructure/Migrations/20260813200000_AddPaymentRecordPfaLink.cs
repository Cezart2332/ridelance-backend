using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// RL-03 — legătura reală între plata înființării și dosarul PFA.
    ///
    /// Până acum „a plătit înființarea?” se răspundea căutând cuvinte în descrierea plății și
    /// comparând sume fixe (30000/45000/79900 bani). Cu plata mutată la finalul completării,
    /// întrebarea devine o poartă: dosarul nu se procesează fără ea. O euristică pe text nu e o
    /// poartă — de asta plata primește o cheie străină.
    ///
    /// Backfill best-effort pentru plățile vechi: se leagă de dosarul aceluiași user, dacă are
    /// exact unul. Restul rămân pe euristică, care se păstrează ca fallback.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddPaymentRecordPfaLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pfa_registration_id",
                schema: "public",
                table: "payment_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_records_pfa_registration_id",
                schema: "public",
                table: "payment_records",
                column: "pfa_registration_id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_records_pfa_registrations_pfa_registration_id",
                schema: "public",
                table: "payment_records",
                column: "pfa_registration_id",
                principalSchema: "public",
                principalTable: "pfa_registrations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("""
                UPDATE public.payment_records p
                SET pfa_registration_id = r.id
                FROM public.pfa_registrations r
                WHERE r.user_id = p.user_id
                  AND p.pfa_registration_id IS NULL
                  AND p.payment_type = 'OneTime'
                  AND (SELECT count(*) FROM public.pfa_registrations x WHERE x.user_id = p.user_id) = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_records_pfa_registrations_pfa_registration_id",
                schema: "public",
                table: "payment_records");

            migrationBuilder.DropIndex(
                name: "ix_payment_records_pfa_registration_id",
                schema: "public",
                table: "payment_records");

            migrationBuilder.DropColumn(
                name: "pfa_registration_id",
                schema: "public",
                table: "payment_records");
        }
    }
}
