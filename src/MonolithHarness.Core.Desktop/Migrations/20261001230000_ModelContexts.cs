using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MonolithHarness.Core.Migrations;

[DbContext(typeof(HarnessDb))]
[Migration("20261001230000_ModelContexts")]
public sealed class ModelContextsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "ModelContextsJson", table: "Providers", type: "TEXT", nullable: false, defaultValue: "{}");
        // Preserve non-default historical limits for the currently saved model only.
        migrationBuilder.Sql("""
            UPDATE Providers SET ModelContextsJson = json_object(trim(Model), json_object('OverrideTokens', ContextLimit))
            WHERE length(trim(Model)) > 0 AND ContextLimit >= 1024
              AND ContextLimit <> 128000 AND NOT (Kind = 'local' AND ContextLimit = 8192);
            """);
    }
    // The bundled SQLite can remove this unindexed column without EF's target-model rebuild.
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("ALTER TABLE Providers DROP COLUMN ModelContextsJson;");
}
