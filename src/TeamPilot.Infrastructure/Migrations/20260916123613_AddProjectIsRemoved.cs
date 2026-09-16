using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectIsRemoved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRemoved",
                table: "Projects",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRemoved",
                table: "Projects");
        }
    }
}
