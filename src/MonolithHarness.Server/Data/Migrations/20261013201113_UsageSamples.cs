using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonolithHarness.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class UsageSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsageSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SampleKey = table.Column<string>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReceivedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Activity = table.Column<string>(type: "TEXT", nullable: false),
                    InputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    CachedInputTokens = table.Column<long>(type: "INTEGER", nullable: true),
                    Seconds = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Estimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ViaServer = table.Column<bool>(type: "INTEGER", nullable: false),
                    Speed = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsageSamples_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageSamples_CompletedUtc",
                table: "UsageSamples",
                column: "CompletedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UsageSamples_UserId_CompletedUtc",
                table: "UsageSamples",
                columns: new[] { "UserId", "CompletedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageSamples_UserId_SampleKey",
                table: "UsageSamples",
                columns: new[] { "UserId", "SampleKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsageSamples");
        }
    }
}
