using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Brewvio.Migrations
{
    /// <inheritdoc />
    public partial class AddModifierAppliesTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppliesTo",
                table: "Modifiers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppliesTo",
                table: "Modifiers");
        }
    }
}
