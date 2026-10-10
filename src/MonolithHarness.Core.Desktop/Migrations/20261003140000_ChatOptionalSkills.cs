using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MonolithHarness.Core.Migrations;

[DbContext(typeof(HarnessDb))]
[Migration("20261003140000_ChatOptionalSkills")]
public sealed class ChatOptionalSkills : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "ChatWebEnabled", table: "Chats", type: "INTEGER", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>(name: "ChatPythonEnabled", table: "Chats", type: "INTEGER", nullable: false, defaultValue: false);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE Chats DROP COLUMN ChatWebEnabled;");
        migrationBuilder.Sql("ALTER TABLE Chats DROP COLUMN ChatPythonEnabled;");
    }
}
