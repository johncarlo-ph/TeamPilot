using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectCloneStatusAndConflictBaseTipSha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloneFailureReason",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true);

            // Every pre-existing row was persisted under the old invariant that a Project was
            // never saved until its initial clone had already succeeded (see
            // ProjectService.CreateAsync's history) - so every row predating this column is, by
            // definition, already fully cloned.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Projects",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Ready");

            migrationBuilder.AddColumn<string>(
                name: "BaseTipSha",
                table: "Conflicts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloneFailureReason",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "BaseTipSha",
                table: "Conflicts");
        }
    }
}
