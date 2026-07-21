using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861
#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingSectionApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "onboarding_section_approvals",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    validated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    validated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_section_approvals", x => x.id);
                    table.ForeignKey(
                        name: "fk_onboarding_section_approvals_pfa_registrations_pfa_registra",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_onboarding_section_approvals_users_validated_by_user_id",
                        column: x => x.validated_by_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_section_approvals_pfa_registration_id_section_key",
                schema: "public",
                table: "onboarding_section_approvals",
                columns: new[] { "pfa_registration_id", "section_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_section_approvals_validated_by_user_id",
                schema: "public",
                table: "onboarding_section_approvals",
                column: "validated_by_user_id");

            // Backfill: PFA-urile deja aprobate sunt considerate complet onboardate,
            // altfel toți clienții existenți ar fi retrimiși în fluxul de onboarding.
            migrationBuilder.Sql("""
                INSERT INTO public.onboarding_section_approvals
                  (id, pfa_registration_id, section_key, status, created_at_utc, validated_at_utc)
                SELECT gen_random_uuid(), p.id, s.key, 'Validated', now(), COALESCE(p.reviewed_at_utc, now())
                FROM public.pfa_registrations p
                CROSS JOIN (VALUES ('AutorizatieTransport'), ('CopieConforma'), ('Vehicul')) AS s(key)
                WHERE p.status = 'Approved';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "onboarding_section_approvals",
                schema: "public");
        }
    }
}
