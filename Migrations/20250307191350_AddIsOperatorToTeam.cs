using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TriviaApp.Migrations
{
    /// <inheritdoc />
    public partial class AddIsOperatorToTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "isoperator",
                table: "teams",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "isoperator",
                table: "teams");
        }
    }
}
