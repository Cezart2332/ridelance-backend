using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// <c>company_profiles</c> — identitatea publică și juridică a unui proprietar de anunțuri.
    ///
    /// O singură tabelă pentru PFA și SRL, discriminată prin <c>owner_type</c>, nu două paralele
    /// (spec §7.1). Slug-ul și <c>user_id</c> sunt unice: un cont are cel mult un profil, iar
    /// <c>/f/{slug}</c> trebuie să ducă mereu la aceeași firmă.
    ///
    /// Nu se face backfill. Un profil gol ar fi apărut public cu o denumire inventată din email;
    /// profilul se creează la prima salvare, iar până atunci anunțurile nu arată niciun
    /// proprietar — vezi <c>GetAllCarsQueryHandler</c>.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddCompanyProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_profiles",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    cui = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    reg_com = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    legal_representative = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    registered_office = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    website = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    public_description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    logo_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    show_phone = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    show_email = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    show_whats_app = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    show_location = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_profiles", x => x.id);
                    // Ștergerea contului duce profilul cu el: nu are sens fără proprietar.
                    table.ForeignKey(
                        name: "fk_company_profiles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_profiles_user_id",
                schema: "public",
                table: "company_profiles",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_profiles_slug",
                schema: "public",
                table: "company_profiles",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "company_profiles", schema: "public");
        }
    }
}
