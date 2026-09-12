using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    LoopBackToStageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MaxLoopIterations = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowStages_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowStages_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowStages_WorkflowStages_LoopBackToStageId",
                        column: x => x.LoopBackToStageId,
                        principalTable: "WorkflowStages",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStages_AgentId",
                table: "WorkflowStages",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStages_LoopBackToStageId",
                table: "WorkflowStages",
                column: "LoopBackToStageId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStages_ProjectId_Order",
                table: "WorkflowStages",
                columns: new[] { "ProjectId", "Order" },
                unique: true);

            // Backfill: every existing project already has exactly one active Research/Design/
            // Coding/Testing agent (self-healed by AgentService.EnsureDefaultAgentsAsync before
            // this migration ever runs), so give each of them the same default workflow
            // WorkflowService.EnsureDefaultWorkflowAsync would create for a brand-new project -
            // Research -> Design -> Coding -> Testing, with Testing looping back to Coding,
            // bounded at 3 total attempts. Without this, an existing project's pipeline page
            // would show empty until its next ticket run lazily self-heals it.
            migrationBuilder.Sql(
                """
                DECLARE @Now DATETIME2 = SYSUTCDATETIME();

                DECLARE @Stages TABLE (
                    Id UNIQUEIDENTIFIER,
                    ProjectId UNIQUEIDENTIFIER,
                    AgentId UNIQUEIDENTIFIER,
                    [Order] INT,
                    Role NVARCHAR(20)
                );

                INSERT INTO @Stages (Id, ProjectId, AgentId, [Order], Role)
                SELECT
                    NEWID(),
                    a.ProjectId,
                    a.Id,
                    CASE a.Role
                        WHEN 'Research' THEN 0
                        WHEN 'Design' THEN 1
                        WHEN 'Coding' THEN 2
                        WHEN 'Testing' THEN 3
                    END,
                    a.Role
                FROM Agents a
                WHERE a.Role IN ('Research', 'Design', 'Coding', 'Testing') AND a.Status = 'Active';

                INSERT INTO WorkflowStages (Id, ProjectId, AgentId, [Order], LoopBackToStageId, MaxLoopIterations, CreatedAtUtc, UpdatedAtUtc)
                SELECT
                    s.Id,
                    s.ProjectId,
                    s.AgentId,
                    s.[Order],
                    CASE WHEN s.Role = 'Testing' THEN (SELECT TOP 1 c.Id FROM @Stages c WHERE c.ProjectId = s.ProjectId AND c.Role = 'Coding') END,
                    CASE WHEN s.Role = 'Testing' THEN 3 END,
                    @Now,
                    NULL
                FROM @Stages s;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowStages");
        }
    }
}
