using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Dosarul serviciilor individuale cumpărate fără onboarding: datele din formular, contul care
    /// a comandat din dashboard și momentul în care arhiva a plecat spre Consulto. Toate nule pe
    /// comenzile vechi, care nu aveau dosar.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddServiceOrderDossier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                schema: "public",
                table: "service_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dossier_json",
                schema: "public",
                table: "service_orders",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "sent_to_consulto_at_utc",
                schema: "public",
                table: "service_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_orders_user_id",
                schema: "public",
                table: "service_orders",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ix_service_orders_user_id", schema: "public", table: "service_orders");
            migrationBuilder.DropColumn(name: "user_id", schema: "public", table: "service_orders");
            migrationBuilder.DropColumn(name: "dossier_json", schema: "public", table: "service_orders");
            migrationBuilder.DropColumn(name: "sent_to_consulto_at_utc", schema: "public", table: "service_orders");
        }
    }
}
