using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MonolithHarness.Core.Migrations;

[DbContext(typeof(HarnessDb))]
[Migration("20261005220000_PendingInputOrder")]
public sealed class PendingInputOrder : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "SortOrder", table: "PendingInputs", type: "INTEGER", nullable: false, defaultValue: 0L);
        migrationBuilder.Sql("UPDATE PendingInputs SET SortOrder = Id;");
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("ALTER TABLE PendingInputs DROP COLUMN SortOrder;");
}
