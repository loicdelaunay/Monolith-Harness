using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MonolithHarness.Core.Migrations;

[DbContext(typeof(HarnessDb))]
[Migration("20261001120000_LocalModels")]
public sealed class LocalModels : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(name: "LocalModelsJson", table: "Providers", type: "TEXT", nullable: false, defaultValue: "{}");
    // This hand-written migration has no target model for EF's SQLite table rebuild.
    // The bundled SQLite supports dropping this unindexed column directly.
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("ALTER TABLE Providers DROP COLUMN LocalModelsJson;");
}
