using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCustomAgentRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AgentRole.Custom was removed - agent creation is now fully system-managed (the 4
            // pipeline agents only). Any existing Custom-role agents (and their instruction
            // history and ticket assignments) can no longer be deserialized, so they're deleted
            // here. The Role column itself stays nvarchar(20); this is a data cleanup, not a
            // schema change.
            migrationBuilder.Sql(
                "DELETE FROM TicketAgentAssignments WHERE AgentId IN (SELECT Id FROM Agents WHERE Role = 'Custom');");
            migrationBuilder.Sql(
                "DELETE FROM Instructions WHERE AgentId IN (SELECT Id FROM Agents WHERE Role = 'Custom');");
            migrationBuilder.Sql(
                "DELETE FROM Agents WHERE Role = 'Custom';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deleted rows cannot be restored.
        }
    }
}
