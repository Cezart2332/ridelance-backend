using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Proprietarul sediului, când nu e solicitantul: emailul lui și acordul semnat din linkul
    /// primit pe email (token hash-uit, momentul, semnătura, IP-ul și dispozitivul).
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddOwnerConsentSigning : Migration
    {
        private const string Table = "company_formation_owners";
        private const string Schema = "public";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "email", schema: Schema, table: Table, type: "character varying(256)", maxLength: 256, nullable: true);
            migrationBuilder.AddColumn<string>(name: "consent_token_hash", schema: Schema, table: Table, type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "consent_sent_at_utc", schema: Schema, table: Table, type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "consent_expires_at_utc", schema: Schema, table: Table, type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "consent_signed_at_utc", schema: Schema, table: Table, type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "consent_signature_document_id", schema: Schema, table: Table, type: "uuid", nullable: true);
            migrationBuilder.AddColumn<string>(name: "consent_text_snapshot", schema: Schema, table: Table, type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "consent_ip_address", schema: Schema, table: Table, type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "consent_user_agent", schema: Schema, table: Table, type: "character varying(512)", maxLength: 512, nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_formation_owners_consent_token_hash",
                schema: Schema,
                table: Table,
                column: "consent_token_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ix_company_formation_owners_consent_token_hash", schema: Schema, table: Table);
            foreach (string column in new[]
            {
                "email", "consent_token_hash", "consent_sent_at_utc", "consent_expires_at_utc", "consent_signed_at_utc",
                "consent_signature_document_id", "consent_text_snapshot", "consent_ip_address", "consent_user_agent",
            })
            {
                migrationBuilder.DropColumn(name: column, schema: Schema, table: Table);
            }
        }
    }
}
