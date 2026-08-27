using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Contractele și procesele-verbale generate pentru o închiriere.
    ///
    /// Versionate, nu suprascrise: dacă se corectează o dată și se regenerează, versiunea veche
    /// rămâne. S-ar putea să fi fost deja trimisă cuiva, iar „ce a semnat clientul" trebuie să
    /// rămână un lucru pe care îl putem arăta.
    ///
    /// Fișierul propriu-zis stă în `documents`, criptat ca oricare altul — tabela asta ține doar
    /// starea documentului în fluxul de semnare.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddGeneratedDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "generated_documents",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rental_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_to_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    signed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    external_signature_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generated_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_generated_documents_rentals_rental_id",
                        column: x => x.rental_id,
                        principalSchema: "public",
                        principalTable: "rentals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_generated_documents_rental_id_generated_at_utc",
                schema: "public",
                table: "generated_documents",
                columns: ["rental_id", "generated_at_utc"],
                descending: [false, true]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "generated_documents", schema: "public");
        }
    }
}
