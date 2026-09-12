using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueTicketBranchNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_ProjectId",
                table: "Tickets");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_ProjectId_BranchName",
                table: "Tickets",
                columns: new[] { "ProjectId", "BranchName" },
                unique: true,
                filter: "[BranchName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_ProjectId_BranchName",
                table: "Tickets");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_ProjectId",
                table: "Tickets",
                column: "ProjectId");
        }
    }
}
