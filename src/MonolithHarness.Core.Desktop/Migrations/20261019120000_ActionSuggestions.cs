using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MonolithHarness.Core.Migrations;

[DbContext(typeof(HarnessDb))]
[Migration("20261019120000_ActionSuggestions")]
public sealed class AddActionSuggestions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("UPDATE States SET EnabledSkills = CASE WHEN EnabledSkills = '' THEN 'action_suggestions' ELSE EnabledSkills || ',action_suggestions' END WHERE instr(',' || EnabledSkills || ',', ',action_suggestions,') = 0;");
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("UPDATE States SET EnabledSkills = trim(replace(',' || EnabledSkills || ',', ',action_suggestions,', ','), ',');");
}
