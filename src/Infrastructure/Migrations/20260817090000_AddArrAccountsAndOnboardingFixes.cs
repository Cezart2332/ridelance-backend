using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Fix-urile de onboarding, partea de schemă:
    ///
    /// 1. <c>arr_accounts</c> — conturile de trezorerie ale agențiilor teritoriale ARR, cu seed
    ///    pentru toate cele 42 de județe. Erau nicăieri: ecranul de plată ARR nu avea ce afișa,
    ///    iar pe două ramuri lipsea de tot.
    /// 2. <c>cod_postal</c> pe cele patru adrese din dosarul de înființare. Sediul social nu se
    ///    poate depune la ONRC fără el.
    /// 3. <c>requires_manual_identity_review</c> pe dosarul PFA — OCR-ul care citește prost
    ///    buletinul nu mai blochează șoferul, doar marchează dosarul.
    /// 4. <c>is_dev_session</c> — dosarele atinse de uneltele de dezvoltare intră în sandbox:
    ///    fără plăți reale, fără emailuri, cu dosare filigranate „TEST".
    ///
    /// Id-urile conturilor sunt UUID v5 derivate din codul de județ, ca o re-rulare a seed-ului
    /// să nu poată insera același județ de două ori.
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddArrAccountsAndOnboardingFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requires_manual_identity_review",
                schema: "public",
                table: "pfa_registrations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Marcajul sesiunilor atinse de uneltele de dezvoltare, ca înregistrările de test să
            // fie filtrabile și ștergibile în bloc.
            migrationBuilder.AddColumn<bool>(
                name: "is_dev_session",
                schema: "public",
                table: "pfa_registrations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            foreach ((string table, string column) in new[]
            {
                ("company_formation_requests", "office_address_cod_postal"),
                ("company_formation_requests", "solicitant_domiciliu_cod_postal"),
                ("company_formation_owners", "persoana_domiciliu_cod_postal"),
                ("consulto_offices", "adresa_cod_postal"),
            })
            {
                migrationBuilder.AddColumn<string>(
                    name: column,
                    schema: "public",
                    table: table,
                    type: "character varying(6)",
                    maxLength: 6,
                    nullable: true);
            }

            migrationBuilder.CreateTable(
                name: "arr_accounts",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    county_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    county_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    treasury = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    fiscal_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arr_accounts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arr_accounts_county_code",
                schema: "public",
                table: "arr_accounts",
                column: "county_code",
                unique: true);

            var seededAtUtc = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);

            string[] columns =
                ["id", "county_code", "county_name", "treasury", "fiscal_code", "iban", "is_active", "created_at_utc", "updated_at_utc"];

            object[][] seed =
            [
                [new Guid("2f76f493-0667-53a4-a78b-480a5da4cb53"), "AB", "Alba", "Trezoreria Alba Iulia", "23831610", "RO07TREZ002501701X006776", true, seededAtUtc, seededAtUtc],
                [new Guid("837af0a0-c7c5-5d7f-bbbc-5f1f6a6cba0d"), "AR", "Arad", "Trezoreria Arad", "23852834", "RO21TREZ021501701X022783", true, seededAtUtc, seededAtUtc],
                [new Guid("153dc158-d94f-56cf-bc2c-220511b30960"), "AG", "Argeș", "Trezoreria Pitești", "23823618", "RO69TREZ046501701X013901", true, seededAtUtc, seededAtUtc],
                [new Guid("7b9efd5b-99a7-5a37-ac93-b0776ec32fc3"), "BC", "Bacău", "Trezoreria Bacău", "23826193", "RO32TREZ061501701X013675", true, seededAtUtc, seededAtUtc],
                [new Guid("a362b38a-6f5e-5ea8-aff6-5f86a1fa9325"), "BH", "Bihor", "Trezoreria Oradea", "23822485", "RO95TREZ076501701X014947", true, seededAtUtc, seededAtUtc],
                [new Guid("5927156c-5786-5ee6-ae21-82c9a413d7d7"), "BN", "Bistrița-Năsăud", "Trezoreria Bistrița", "23811109", "RO15TREZ101501701X008660", true, seededAtUtc, seededAtUtc],
                [new Guid("06073f22-ee83-58ea-bc6e-8bf234b4a7e6"), "BT", "Botoșani", "Trezoreria Botoșani", "23902070", "RO77TREZ116501701X007622", true, seededAtUtc, seededAtUtc],
                [new Guid("2296123e-5790-5401-a03b-ab579075212c"), "BV", "Brașov", "Trezoreria Brașov", "23860730", "RO97TREZ131501701X015876", true, seededAtUtc, seededAtUtc],
                [new Guid("f6f1e818-fa01-580f-bf8e-159a1ddab635"), "BR", "Brăila", "Trezoreria Brăila", "23873330", "RO68TREZ151501701X008936", true, seededAtUtc, seededAtUtc],
                [new Guid("2f3691fd-d1f2-56b6-9637-a3098e7677df"), "B", "București", "Trezoreria Statului Sector 5", "27364739", "RO37TREZ705501701X008844", true, seededAtUtc, seededAtUtc],
                [new Guid("b3f6b86c-793e-5756-af1f-13cd44a9419b"), "BZ", "Buzău", "Trezoreria Buzău", "23870172", "RO49TREZ166501701X011199", true, seededAtUtc, seededAtUtc],
                [new Guid("8833fa36-8bea-5cb2-8e66-967396b96cfa"), "CS", "Caraș-Severin", "Trezoreria Reșița", "23818980", "RO61TREZ181501701X005726", true, seededAtUtc, seededAtUtc],
                [new Guid("e1cd9037-8c04-5ee5-af62-85dd5b111428"), "CL", "Călărași", "Trezoreria Călărași", "23839606", "RO44TREZ201501701X005360", true, seededAtUtc, seededAtUtc],
                [new Guid("50dc9e07-c7c8-583c-aba0-e92a6c1edfb5"), "CJ", "Cluj", "Trezoreria Cluj-Napoca", "23826223", "RO93TREZ216501701X030552", true, seededAtUtc, seededAtUtc],
                [new Guid("cad8de36-8f99-51cc-b291-7062b8a6fa2f"), "CT", "Constanța", "Trezoreria Constanța", "23856208", "RO62TREZ231501701X023622", true, seededAtUtc, seededAtUtc],
                [new Guid("60340cdc-e6ea-520f-80be-46b9c7ad2b88"), "CV", "Covasna", "Trezoreria Sfântu Gheorghe", "23819170", "RO75TREZ256501701X006543", true, seededAtUtc, seededAtUtc],
                [new Guid("9496c5ea-de23-5c1b-821a-0a9443eb5e04"), "DB", "Dâmbovița", "Trezoreria Târgoviște", "23886756", "RO33TREZ271501701X008638", true, seededAtUtc, seededAtUtc],
                [new Guid("1ed3d49c-45da-5334-afc9-96a3516b34fd"), "DJ", "Dolj", "Trezoreria Craiova", "23828933", "RO98TREZ291501701X017175", true, seededAtUtc, seededAtUtc],
                [new Guid("f90df21d-0f1b-540e-84dc-fff1ca7e4810"), "GL", "Galați", "Trezoreria Galați", "23812074", "RO74TREZ306501701X013999", true, seededAtUtc, seededAtUtc],
                [new Guid("1d47ea60-f5ef-5f2f-be35-a6e1c251547a"), "GR", "Giurgiu", "Trezoreria Giurgiu", "23872173", "RO05TREZ321501701X008723", true, seededAtUtc, seededAtUtc],
                [new Guid("2ce87ad4-1f92-5b30-8f75-5ad85302c78c"), "GJ", "Gorj", "Trezoreria Târgu Jiu", "23828968", "RO30TREZ336501701X008448", true, seededAtUtc, seededAtUtc],
                [new Guid("d1549449-9777-5896-8fe2-8eb3e49a185d"), "HR", "Harghita", "Trezoreria Miercurea Ciuc", "23825180", "RO25TREZ351501701X004833", true, seededAtUtc, seededAtUtc],
                [new Guid("13b5d9df-41ea-5a6d-be3d-6baae2febd66"), "HD", "Hunedoara", "Trezoreria Deva", "23818913", "RO90TREZ366501701X009561", true, seededAtUtc, seededAtUtc],
                [new Guid("ea3ac657-6c37-577a-bd30-c6d51f6d2192"), "IL", "Ialomița", "Trezoreria Slobozia", "23891035", "RO98TREZ391501701X006443", true, seededAtUtc, seededAtUtc],
                [new Guid("d6880a1d-7202-5aeb-8a6a-a072b6860e7a"), "IS", "Iași", "Trezoreria Iași", "23817055", "RO92TREZ406501701X020597", true, seededAtUtc, seededAtUtc],
                [new Guid("582bfde8-521c-5ca2-bd4f-84f075f1ef26"), "IF", "Ilfov", "Trezoreria Ilfov", "23888021", "RO70TREZ421501701X008461", true, seededAtUtc, seededAtUtc],
                [new Guid("3fd10522-179c-51d1-88a3-23b61983daf6"), "MM", "Maramureș", "Trezoreria Baia Mare", "23845632", "RO33TREZ436501701X013376", true, seededAtUtc, seededAtUtc],
                [new Guid("f1ce6883-2357-53cb-b55d-ef8e8decd50c"), "MH", "Mehedinți", "Trezoreria Drobeta-Turnu Severin", "23862005", "RO26TREZ461501701X006163", true, seededAtUtc, seededAtUtc],
                [new Guid("c865184f-716d-54c4-a10f-c00a91c53965"), "MS", "Mureș", "Trezoreria Mureș", "23872190", "RO84TREZ476501701X014897", true, seededAtUtc, seededAtUtc],
                [new Guid("726f4c30-8d80-565b-830b-809ab092cf42"), "NT", "Neamț", "Trezoreria Neamț", "13220458", "RO47TREZ491501701X014477", true, seededAtUtc, seededAtUtc],
                [new Guid("e6711674-f83b-5840-91ef-0a27960d457e"), "OT", "Olt", "Trezoreria Slatina", "23839410", "RO03TREZ506501701X009236", true, seededAtUtc, seededAtUtc],
                [new Guid("531eb41f-5c87-578e-8d1b-416d3f3089c7"), "PH", "Prahova", "Trezoreria Ploiești", "23835558", "RO84TREZ521501701X013579", true, seededAtUtc, seededAtUtc],
                [new Guid("dd31877d-7725-5670-9ad6-6ae5f433136c"), "SM", "Satu Mare", "Trezoreria Satu Mare", "23906110", "RO51TREZ546501701X010035", true, seededAtUtc, seededAtUtc],
                [new Guid("e58ef344-5a29-5cd9-a157-866a16456d01"), "SJ", "Sălaj", "Trezoreria Zalău", "23845535", "RO36TREZ561501701X007861", true, seededAtUtc, seededAtUtc],
                [new Guid("0c918fb4-b514-5fba-8467-a744525519e9"), "SB", "Sibiu", "Trezoreria Sibiu", "23823626", "RO84TREZ576501701X018812", true, seededAtUtc, seededAtUtc],
                [new Guid("7fc6c342-8263-5ee7-9bf2-914cb4efe9e7"), "SV", "Suceava", "Trezoreria Suceava", "23828585", "RO59TREZ591501701X007603", true, seededAtUtc, seededAtUtc],
                [new Guid("6b1638ec-bbff-50fd-8f36-873e41886461"), "TR", "Teleorman", "Trezoreria Alexandria", "23837621", "RO24TREZ606501701X007244", true, seededAtUtc, seededAtUtc],
                [new Guid("15b5711a-4f74-58ce-8b35-f55ff702ed8c"), "TM", "Timiș", "Trezoreria Timișoara", "23864430", "RO49TREZ621501701X019385", true, seededAtUtc, seededAtUtc],
                [new Guid("5edceaca-c96e-5ff7-857b-26c89beff94a"), "TL", "Tulcea", "Trezoreria Tulcea", "23877260", "RO03TREZ641501701X006931", true, seededAtUtc, seededAtUtc],
                [new Guid("c369e91e-6252-55a9-bd36-69315b99bc72"), "VS", "Vaslui", "Trezoreria Vaslui", "23889639", "RO94TREZ656501701X005177", true, seededAtUtc, seededAtUtc],
                [new Guid("8cdab648-cfcd-537e-80ca-fbc317f7a4ac"), "VL", "Vâlcea", "Trezoreria Vâlcea", "23830585", "RO58TREZ671501701X010656", true, seededAtUtc, seededAtUtc],
                [new Guid("b020a9fb-77cc-59c4-95e8-294e88235fac"), "VN", "Vrancea", "Trezoreria Focșani", "23829440", "RO60TREZ691501701X008590", true, seededAtUtc, seededAtUtc],
            ];

            foreach (object[] row in seed)
            {
                migrationBuilder.InsertData(
                    schema: "public",
                    table: "arr_accounts",
                    columns: columns,
                    values: row);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "arr_accounts", schema: "public");

            migrationBuilder.DropColumn(name: "adresa_cod_postal", schema: "public", table: "consulto_offices");
            migrationBuilder.DropColumn(name: "persoana_domiciliu_cod_postal", schema: "public", table: "company_formation_owners");
            migrationBuilder.DropColumn(name: "solicitant_domiciliu_cod_postal", schema: "public", table: "company_formation_requests");
            migrationBuilder.DropColumn(name: "office_address_cod_postal", schema: "public", table: "company_formation_requests");

            migrationBuilder.DropColumn(name: "is_dev_session", schema: "public", table: "pfa_registrations");

            migrationBuilder.DropColumn(
                name: "requires_manual_identity_review",
                schema: "public",
                table: "pfa_registrations");
        }
    }
}
