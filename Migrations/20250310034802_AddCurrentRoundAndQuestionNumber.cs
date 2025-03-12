using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TriviaApp.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrentRoundAndQuestionNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "currentquestionnumber",
                table: "games",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "currentround",
                table: "games",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "currentquestionnumber",
                table: "games");

            migrationBuilder.DropColumn(
                name: "currentround",
                table: "games");
        }
    }
}
