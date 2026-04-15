using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCoupleGuestFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CoupleJoinDataMigrated",
                table: "Users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCoupleGuest",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoupleJoinDataMigrated",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsCoupleGuest",
                table: "Users");
        }
    }
}
