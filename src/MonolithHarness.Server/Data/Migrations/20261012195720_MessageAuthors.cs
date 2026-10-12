using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonolithHarness.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class MessageAuthors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessageAuthors",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChatId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAuthors", x => new { x.ProjectId, x.MessageId });
                    table.ForeignKey(
                        name: "FK_MessageAuthors_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MessageAuthors_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageAuthors_ProjectId_ChatId",
                table: "MessageAuthors",
                columns: new[] { "ProjectId", "ChatId" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageAuthors_UserId",
                table: "MessageAuthors",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageAuthors");
        }
    }
}
