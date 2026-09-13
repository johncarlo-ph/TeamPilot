using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMessageCreatedTicketId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedTicketId",
                table: "ChatMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_CreatedTicketId",
                table: "ChatMessages",
                column: "CreatedTicketId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Tickets_CreatedTicketId",
                table: "ChatMessages",
                column: "CreatedTicketId",
                principalTable: "Tickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Tickets_CreatedTicketId",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_CreatedTicketId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "CreatedTicketId",
                table: "ChatMessages");
        }
    }
}
