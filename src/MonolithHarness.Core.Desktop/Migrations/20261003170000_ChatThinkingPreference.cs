using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MonolithHarness.Core.Migrations;

[DbContext(typeof(HarnessDb))]
[Migration("20261003170000_ChatThinkingPreference")]
public sealed class ChatThinkingPreference : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "ChatThinkingLevel", table: "States", type: "TEXT", nullable: false, defaultValue: "none");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE States DROP COLUMN ChatThinkingLevel;");
}
