using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MonolithHarness.Core.Migrations;

[DbContext(typeof(HarnessDb))]
[Migration("20261001120000_LocalModels")]
public sealed class LocalModels : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(name: "LocalModelsJson", table: "Providers", type: "TEXT", nullable: false, defaultValue: "{}");
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "LocalModelsJson", table: "Providers");
}
