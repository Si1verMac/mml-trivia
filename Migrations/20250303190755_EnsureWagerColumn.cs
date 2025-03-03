using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TriviaApp.Migrations
{
    /// <inheritdoc />
    public partial class EnsureWagerColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Wager",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuestionNumber = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GameTeamGameId = table.Column<int>(type: "integer", nullable: true),
                    GameTeamTeamId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wager", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wager_GameTeams_GameTeamGameId_GameTeamTeamId",
                        columns: x => new { x.GameTeamGameId, x.GameTeamTeamId },
                        principalTable: "GameTeams",
                        principalColumns: new[] { "GameId", "TeamId" });
                    table.ForeignKey(
                        name: "FK_Wager_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Wager_GameTeamGameId_GameTeamTeamId",
                table: "Wager",
                columns: new[] { "GameTeamGameId", "GameTeamTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_Wager_TeamId",
                table: "Wager",
                column: "TeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Wager");
        }
    }
}
