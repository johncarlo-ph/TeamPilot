using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IntroduceSprintAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Projects_ProjectId",
                table: "Tickets");

            // Sprints table is created - and backfilled with one default sprint per existing
            // project, carrying its old BaseBranch/sprint fields - before those columns are
            // dropped from Projects below, and before Tickets.SprintId is added, so both backfill
            // steps have the data they need to copy from.
            migrationBuilder.CreateTable(
                name: "Sprints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BaseBranch = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SprintStartDate = table.Column<DateTime>(type: "date", nullable: true),
                    SprintEndDate = table.Column<DateTime>(type: "date", nullable: true),
                    SprintGoal = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsRemoved = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sprints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sprints_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(@"
                INSERT INTO [Sprints] ([Id], [ProjectId], [Name], [BaseBranch], [SprintStartDate], [SprintEndDate], [SprintGoal], [IsRemoved], [CreatedAtUtc], [UpdatedAtUtc])
                SELECT NEWID(), [Id], [Name] + N' Sprint', [BaseBranch], [SprintStartDate], [SprintEndDate], [SprintGoal], 0, [CreatedAtUtc], [UpdatedAtUtc]
                FROM [Projects];
            ");

            migrationBuilder.AddColumn<Guid>(
                name: "SprintId",
                table: "Tickets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE t
                SET t.[SprintId] = s.[Id]
                FROM [Tickets] t
                INNER JOIN [Sprints] s ON s.[ProjectId] = t.[ProjectId];
            ");

            migrationBuilder.AlterColumn<Guid>(
                name: "SprintId",
                table: "Tickets",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "BaseBranch",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SprintEndDate",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SprintGoal",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SprintStartDate",
                table: "Projects");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_SprintId",
                table: "Tickets",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_Sprints_ProjectId",
                table: "Sprints",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Projects_ProjectId",
                table: "Tickets",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Sprints_SprintId",
                table: "Tickets",
                column: "SprintId",
                principalTable: "Sprints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Projects_ProjectId",
                table: "Tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Sprints_SprintId",
                table: "Tickets");

            migrationBuilder.DropTable(
                name: "Sprints");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_SprintId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "SprintId",
                table: "Tickets");

            migrationBuilder.AddColumn<string>(
                name: "BaseBranch",
                table: "Projects",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

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

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Projects_ProjectId",
                table: "Tickets",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
