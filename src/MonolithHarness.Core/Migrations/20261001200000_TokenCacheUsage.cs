using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MonolithHarness.Core.Migrations;

[DbContext(typeof(HarnessDb))]
[Migration("20261001200000_TokenCacheUsage")]
public sealed class TokenCacheUsage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "CachedInputTokens", table: "Messages", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<long>(name: "CachedInputTokens", table: "TokenUsages", type: "INTEGER", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CachedInputTokens", table: "Messages");
        migrationBuilder.DropColumn(name: "CachedInputTokens", table: "TokenUsages");
    }
}
