using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MonolithHarness.Core.Migrations;

[DbContext(typeof(HarnessDb))]
[Migration("20261023120000_ProjectInstructions")]
public sealed class AddProjectInstructions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "Instructions", table: "Projects", type: "TEXT", nullable: false, defaultValue: "");
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "Instructions", table: "Projects");
}
