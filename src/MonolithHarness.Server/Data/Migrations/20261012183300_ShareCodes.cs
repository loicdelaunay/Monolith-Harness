using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonolithHarness.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShareCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Shares",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Shares",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Shares_Code",
                table: "Shares",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shares_IsPublic",
                table: "Shares",
                column: "IsPublic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shares_Code",
                table: "Shares");

            migrationBuilder.DropIndex(
                name: "IX_Shares_IsPublic",
                table: "Shares");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Shares");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Shares");
        }
    }
}
