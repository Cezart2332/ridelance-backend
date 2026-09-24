using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Răspunsurile din onboarding, câte un rând la fiecare schimbare, ca adminul să vadă tot
    /// parcursul (și ce a schimbat omul pe drum).
    ///
    /// Scrisă de mână, ca toate migrațiile din proiect.
    /// </summary>
    public partial class AddOnboardingAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "onboarding_answers",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    question_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    question = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    value_label = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    answered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table => table.PrimaryKey("pk_onboarding_answers", x => x.id));

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_answers_user_id_question_id_answered_at_utc",
                schema: "public",
                table: "onboarding_answers",
                columns: new[] { "user_id", "question_id", "answered_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "onboarding_answers", schema: "public");
        }
    }
}
