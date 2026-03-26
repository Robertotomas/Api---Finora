using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSavingsObjectiveTargetDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "TargetDate",
                table: "SavingsObjectives",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetDate",
                table: "SavingsObjectives");
        }
    }
}
