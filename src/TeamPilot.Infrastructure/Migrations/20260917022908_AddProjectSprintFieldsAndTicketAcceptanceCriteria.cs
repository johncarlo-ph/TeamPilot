using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectSprintFieldsAndTicketAcceptanceCriteria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfilled via the column's own DEFAULT constraint (SQL Server applies it to every
            // existing row when a NOT NULL column is added to a populated table) - existing
            // tickets predate this field, so they get a placeholder rather than an empty string,
            // which would be indistinguishable from acceptance criteria nobody ever wrote.
            migrationBuilder.AddColumn<string>(
                name: "AcceptanceCriteria",
                table: "Tickets",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "Not documented - added before acceptance criteria became a required field.");

            migrationBuilder.AddColumn<DateTime>(
                name: "SprintEndDate",
                table: "Projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SprintGoal",
                table: "Projects",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SprintStartDate",
                table: "Projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposedTicketAcceptanceCriteria",
                table: "ChatMessages",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptanceCriteria",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "SprintEndDate",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SprintGoal",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SprintStartDate",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ProposedTicketAcceptanceCriteria",
                table: "ChatMessages");
        }
    }
}
