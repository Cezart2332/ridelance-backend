using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddArrAuthorizationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "arr_authorization_requests",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    fee_snapshot_bani = table.Column<long>(type: "bigint", nullable: false),
                    submission_method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    dossier_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dossier_generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    authorization_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    authorization_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    authorization_issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    authorization_expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    admin_note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arr_authorization_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_arr_authorization_requests_pfa_registrations_pfa_registrati",
                        column: x => x.pfa_registration_id,
                        principalSchema: "public",
                        principalTable: "pfa_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arr_authorization_requests_pfa_registration_id",
                schema: "public",
                table: "arr_authorization_requests",
                column: "pfa_registration_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "arr_authorization_requests", schema: "public");
        }
    }
}
