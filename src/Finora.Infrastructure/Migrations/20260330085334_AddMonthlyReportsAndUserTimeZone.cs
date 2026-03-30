using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyReportsAndUserTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MonthlyReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FileRelativePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyReports_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyReports_HouseholdId_Year_Month",
                table: "MonthlyReports",
                columns: new[] { "HouseholdId", "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthlyReports");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Users");
        }
    }
}
