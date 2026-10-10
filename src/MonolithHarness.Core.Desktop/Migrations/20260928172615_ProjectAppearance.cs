using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonolithHarness.Core.Migrations
{
    /// <inheritdoc />
    public partial class ProjectAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Projects",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "Projects",
                type: "TEXT",
                nullable: false,
                defaultValue: "folder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "Projects");
        }
    }
}
