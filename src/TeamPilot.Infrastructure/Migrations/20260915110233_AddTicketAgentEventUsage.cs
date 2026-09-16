using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketAgentEventUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "TicketAgentEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                table: "TicketAgentEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                table: "TicketAgentEvents",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "TicketAgentEvents");

            migrationBuilder.DropColumn(
                name: "InputTokens",
                table: "TicketAgentEvents");

            migrationBuilder.DropColumn(
                name: "OutputTokens",
                table: "TicketAgentEvents");
        }
    }
}
