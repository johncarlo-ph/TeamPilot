using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStageExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StageExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Output = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageExecutions_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StageExecutions_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StageExecutions_AgentId",
                table: "StageExecutions",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_StageExecutions_TicketId_AgentId_CreatedAtUtc",
                table: "StageExecutions",
                columns: new[] { "TicketId", "AgentId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StageExecutions");
        }
    }
}
