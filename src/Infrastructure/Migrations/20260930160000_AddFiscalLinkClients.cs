using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Clientul FiscalLink al fiecărui PFA conectat. Doar ID-ul de la FiscalLink: codul de activare și
/// casele de marcat se citesc de acolo, ca să nu existe două versiuni ale lor.
/// </summary>
public partial class AddFiscalLinkClients : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "fiscal_link_clients",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                pfa_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                fiscal_link_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_fiscal_link_clients", x => x.id);
                table.ForeignKey(
                    name: "fk_fiscal_link_clients_pfa_registrations_pfa_registration_id",
                    column: x => x.pfa_registration_id,
                    principalSchema: "public",
                    principalTable: "pfa_registrations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_fiscal_link_clients_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_fiscal_link_clients_pfa_registration_id",
            schema: "public",
            table: "fiscal_link_clients",
            column: "pfa_registration_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_fiscal_link_clients_user_id",
            schema: "public",
            table: "fiscal_link_clients",
            column: "user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "fiscal_link_clients",
            schema: "public");
    }
}
