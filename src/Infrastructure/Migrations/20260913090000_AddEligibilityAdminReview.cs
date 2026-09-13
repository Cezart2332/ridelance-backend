using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Verdictul adminului pe pasul de eligibilitate.
    ///
    /// Pasul se bifa doar pe evaluarea automată din datele extrase, deci n-avea buton în admin.
    /// Coloanele țin validarea și respingerea separat de <c>status</c>, care rămâne evaluarea
    /// automată.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddEligibilityAdminReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "admin_validated_at_utc",
                schema: "public",
                table: "onboarding_eligibility_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "admin_validated_by_user_id",
                schema: "public",
                table: "onboarding_eligibility_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "admin_rejected_at_utc",
                schema: "public",
                table: "onboarding_eligibility_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "admin_review_note",
                schema: "public",
                table: "onboarding_eligibility_profiles",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "admin_validated_at_utc", schema: "public", table: "onboarding_eligibility_profiles");
            migrationBuilder.DropColumn(
                name: "admin_validated_by_user_id", schema: "public", table: "onboarding_eligibility_profiles");
            migrationBuilder.DropColumn(
                name: "admin_rejected_at_utc", schema: "public", table: "onboarding_eligibility_profiles");
            migrationBuilder.DropColumn(
                name: "admin_review_note", schema: "public", table: "onboarding_eligibility_profiles");
        }
    }
}
