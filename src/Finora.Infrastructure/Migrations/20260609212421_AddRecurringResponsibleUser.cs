using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringResponsibleUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResponsibleUserId",
                table: "RecurringTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTransactions_ResponsibleUserId",
                table: "RecurringTransactions",
                column: "ResponsibleUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringTransactions_Users_ResponsibleUserId",
                table: "RecurringTransactions",
                column: "ResponsibleUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurringTransactions_Users_ResponsibleUserId",
                table: "RecurringTransactions");

            migrationBuilder.DropIndex(
                name: "IX_RecurringTransactions_ResponsibleUserId",
                table: "RecurringTransactions");

            migrationBuilder.DropColumn(
                name: "ResponsibleUserId",
                table: "RecurringTransactions");
        }
    }
}
