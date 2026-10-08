using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MonolithHarness.Core.Migrations;

[DbContext(typeof(HarnessDb))]
[Migration("20261008120000_ConversationResourceBoundary")]
public sealed class ConversationResourceBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<bool>(
        name: "AllowOutsideResources", table: "Chats", type: "INTEGER", nullable: false, defaultValue: true);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("ALTER TABLE Chats DROP COLUMN AllowOutsideResources;");
}
