using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <summary>
/// Record-ul declarației (spec declarații §4, §6): indexul ANAF, hash-ul XML-ului, versiunea regulilor
/// și a validatorului pe versiune, plus task-urile de rectificare.
/// </summary>
public partial class AddDeclarationRecordFields : Migration
{
    private const string Schema = "public";
    private const string Versions = "declaration_versions";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "anaf_index", schema: Schema, table: Versions, type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "xml_hash", schema: Schema, table: Versions, type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ruleset_version", schema: Schema, table: Versions, type: "character varying(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>(name: "validator_version", schema: Schema, table: Versions, type: "character varying(64)", maxLength: 64, nullable: true);

        migrationBuilder.CreateTable(
            name: "declaration_rectification_tasks",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                declaration_id = table.Column<Guid>(type: "uuid", nullable: false),
                accepted_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                diff_json = table.Column<string>(type: "jsonb", nullable: false),
                detected_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                resolved_by_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_declaration_rectification_tasks", x => x.id);
                table.ForeignKey("fk_declaration_rectification_tasks_declarations_declaration_id", x => x.declaration_id, "declarations", "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_declaration_rectification_tasks_declaration_versions_accepted_version_id", x => x.accepted_version_id, Versions, "id", Schema, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_declaration_rectification_tasks_declaration_versions_resolved_by_version_id", x => x.resolved_by_version_id, Versions, "id", Schema, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "ix_declaration_rectification_tasks_declaration_id", schema: Schema, table: "declaration_rectification_tasks", column: "declaration_id");
        migrationBuilder.CreateIndex(name: "ix_declaration_rectification_tasks_accepted_version_id", schema: Schema, table: "declaration_rectification_tasks", column: "accepted_version_id");
        migrationBuilder.CreateIndex(name: "ix_declaration_rectification_tasks_resolved_by_version_id", schema: Schema, table: "declaration_rectification_tasks", column: "resolved_by_version_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "declaration_rectification_tasks", schema: Schema);
        migrationBuilder.DropColumn(name: "anaf_index", schema: Schema, table: Versions);
        migrationBuilder.DropColumn(name: "xml_hash", schema: Schema, table: Versions);
        migrationBuilder.DropColumn(name: "ruleset_version", schema: Schema, table: Versions);
        migrationBuilder.DropColumn(name: "validator_version", schema: Schema, table: Versions);
    }
}
