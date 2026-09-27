using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Documentele de platformă (facturi de comision, rapoarte): ștergere logică de către admin. Indexul
    /// unic pe fișier ignoră documentele șterse, ca același PDF să se poată reîncărca.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddPlatformDocumentSoftDelete : Migration
    {
        private const string HashIndex = "ix_platform_documents_pfa_registration_id_file_hash";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                schema: "public",
                table: "platform_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_user_id",
                schema: "public",
                table: "platform_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.DropIndex(name: HashIndex, schema: "public", table: "platform_documents");
            migrationBuilder.CreateIndex(
                name: HashIndex,
                schema: "public",
                table: "platform_documents",
                columns: ["pfa_registration_id", "file_hash"],
                unique: true,
                filter: "deleted_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: HashIndex, schema: "public", table: "platform_documents");
            migrationBuilder.CreateIndex(
                name: HashIndex,
                schema: "public",
                table: "platform_documents",
                columns: ["pfa_registration_id", "file_hash"],
                unique: true);
            migrationBuilder.DropColumn(name: "deleted_at_utc", schema: "public", table: "platform_documents");
            migrationBuilder.DropColumn(name: "deleted_by_user_id", schema: "public", table: "platform_documents");
        }
    }
}
