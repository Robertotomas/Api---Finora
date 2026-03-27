using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdPrimaryAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PrimaryAccountId",
                table: "Households",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Households_PrimaryAccountId",
                table: "Households",
                column: "PrimaryAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Households_Accounts_PrimaryAccountId",
                table: "Households",
                column: "PrimaryAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Households_Accounts_PrimaryAccountId",
                table: "Households");

            migrationBuilder.DropIndex(
                name: "IX_Households_PrimaryAccountId",
                table: "Households");

            migrationBuilder.DropColumn(
                name: "PrimaryAccountId",
                table: "Households");
        }
    }
}
