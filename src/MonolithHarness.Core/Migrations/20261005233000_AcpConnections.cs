using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MonolithHarness.Core.Migrations;

[DbContext(typeof(HarnessDb))]
[Migration("20261005233000_AcpConnections")]
public sealed class AcpConnections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(name: "AcpArgumentsJson", table: "Providers", type: "TEXT", nullable: false, defaultValue: "[]");
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("ALTER TABLE Providers DROP COLUMN AcpArgumentsJson;");
}
