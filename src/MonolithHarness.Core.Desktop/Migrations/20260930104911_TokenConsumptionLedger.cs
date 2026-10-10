using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonolithHarness.Core.Migrations
{
    /// <inheritdoc />
    public partial class TokenConsumptionLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TokenUsages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProviderName = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Activity = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProjectName = table.Column<string>(type: "TEXT", nullable: false),
                    ChatId = table.Column<int>(type: "INTEGER", nullable: true),
                    ChatTitle = table.Column<string>(type: "TEXT", nullable: false),
                    InputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    InputEstimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    OutputEstimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    Seconds = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Legacy = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenUsages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_CompletedUtc",
                table: "TokenUsages",
                column: "CompletedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_ProviderId_Model_CompletedUtc",
                table: "TokenUsages",
                columns: new[] { "ProviderId", "Model", "CompletedUtc" });
            // Import once, without inventing dates or the provider/model absent from old messages.
            migrationBuilder.Sql("""
                INSERT INTO TokenUsages
                    (StartedUtc, CompletedUtc, ProviderId, ProviderName, Model, Activity, ProjectId, ProjectName,
                     ChatId, ChatTitle, InputTokens, OutputTokens, InputEstimated, OutputEstimated, Seconds, Status, Legacy)
                SELECT m.CompletedUtc, m.CompletedUtc, NULL, '', '',
                       CASE WHEN m.Role = 'compaction' THEN 'compaction' ELSE 'chat' END,
                       c.ProjectId, COALESCE(p.Name, ''), m.ChatId, COALESCE(c.Title, ''),
                       MAX(0, COALESCE(m.InputTokens, 0)), MAX(0, COALESCE(m.OutputTokens, 0)),
                       m.InputTokens IS NULL, m.OutputTokens IS NULL, m.Seconds,
                       CASE WHEN m.State IN ('complete', 'compacted') THEN 'complete' ELSE 'cancelled' END, 1
                FROM (
                    SELECT *, ROW_NUMBER() OVER (
                        PARTITION BY CompletedUtc, Role, InputTokens, OutputTokens, Seconds, Content ORDER BY Id
                    ) AS UsageCopy
                    FROM Messages
                    WHERE CompletedUtc IS NOT NULL AND Role IN ('assistant', 'compaction')
                          AND (InputTokens IS NOT NULL OR OutputTokens IS NOT NULL)
                ) m LEFT JOIN Chats c ON c.Id = m.ChatId LEFT JOIN Projects p ON p.Id = c.ProjectId
                WHERE m.UsageCopy = 1;
                UPDATE States SET FeaturesJson = json_set(
                    CASE WHEN json_valid(FeaturesJson) THEN FeaturesJson ELSE '{}' END,
                    '$.ConsumptionLegacyMessageId', COALESCE((SELECT MAX(Id) FROM Messages), 0));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE States SET FeaturesJson = json_remove(FeaturesJson, '$.ConsumptionLegacyMessageId') WHERE json_valid(FeaturesJson);");
            migrationBuilder.DropTable(
                name: "TokenUsages");
        }
    }
}
