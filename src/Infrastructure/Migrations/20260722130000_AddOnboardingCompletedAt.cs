using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "onboarding_completed_at_utc",
                schema: "public",
                table: "pfa_registrations",
                type: "timestamp with time zone",
                nullable: true);

            // Grandfathering: PFA-urile deja aprobate au (prin migrarea anterioară + fluxul aplicației)
            // toate secțiunile de documente validate, deci erau deja considerate înrolate.
            // Le marcăm complete ca să NU regreseze în „în onboarding" după decuplarea înrolării de Status.
            migrationBuilder.Sql("""
                UPDATE public.pfa_registrations
                SET onboarding_completed_at_utc = COALESCE(reviewed_at_utc, now())
                WHERE status = 'Approved';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "onboarding_completed_at_utc",
                schema: "public",
                table: "pfa_registrations");
        }
    }
}
