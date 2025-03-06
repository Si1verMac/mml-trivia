using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TriviaApp.Migrations
{
    /// <inheritdoc />
    public partial class UpdateGameQuestionAndGameTeamSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "questions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    text = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    options = table.Column<string[]>(type: "text[]", nullable: false),
                    correctanswer = table.Column<string>(type: "text", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_questions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password = table.Column<string>(type: "text", nullable: false),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    currentquestionid = table.Column<int>(type: "integer", nullable: true),
                    createdat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    startedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    endedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    currentquestionindex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_games", x => x.id);
                    table.ForeignKey(
                        name: "FK_games_questions_currentquestionid",
                        column: x => x.currentquestionid,
                        principalTable: "questions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "answers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    gameid = table.Column<int>(type: "integer", nullable: false),
                    teamid = table.Column<int>(type: "integer", nullable: false),
                    questionid = table.Column<int>(type: "integer", nullable: false),
                    selectedanswer = table.Column<string>(type: "text", nullable: true),
                    wager = table.Column<int>(type: "integer", nullable: true),
                    iscorrect = table.Column<bool>(type: "boolean", nullable: true),
                    submittedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_answers", x => x.id);
                    table.ForeignKey(
                        name: "FK_answers_games_gameid",
                        column: x => x.gameid,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_answers_questions_questionid",
                        column: x => x.questionid,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_answers_teams_teamid",
                        column: x => x.teamid,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gamequestions",
                columns: table => new
                {
                    gameid = table.Column<int>(type: "integer", nullable: false),
                    questionid = table.Column<int>(type: "integer", nullable: false),
                    orderindex = table.Column<int>(type: "integer", nullable: false),
                    isanswered = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gamequestions", x => new { x.gameid, x.questionid });
                    table.ForeignKey(
                        name: "FK_gamequestions_games_gameid",
                        column: x => x.gameid,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gamequestions_questions_questionid",
                        column: x => x.questionid,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gameteams",
                columns: table => new
                {
                    gameid = table.Column<int>(type: "integer", nullable: false),
                    teamid = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gameteams", x => new { x.gameid, x.teamid });
                    table.ForeignKey(
                        name: "FK_gameteams_games_gameid",
                        column: x => x.gameid,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gameteams_teams_teamid",
                        column: x => x.teamid,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_answers_gameid",
                table: "answers",
                column: "gameid");

            migrationBuilder.CreateIndex(
                name: "IX_answers_questionid",
                table: "answers",
                column: "questionid");

            migrationBuilder.CreateIndex(
                name: "IX_answers_teamid",
                table: "answers",
                column: "teamid");

            migrationBuilder.CreateIndex(
                name: "IX_gamequestions_questionid",
                table: "gamequestions",
                column: "questionid");

            migrationBuilder.CreateIndex(
                name: "IX_games_currentquestionid",
                table: "games",
                column: "currentquestionid");

            migrationBuilder.CreateIndex(
                name: "IX_gameteams_teamid",
                table: "gameteams",
                column: "teamid");

            migrationBuilder.CreateIndex(
                name: "IX_teams_name",
                table: "teams",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "answers");

            migrationBuilder.DropTable(
                name: "gamequestions");

            migrationBuilder.DropTable(
                name: "gameteams");

            migrationBuilder.DropTable(
                name: "games");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "questions");
        }
    }
}
