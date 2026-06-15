using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: a previous Stripe attempt may have already added these columns to the live DB.
            migrationBuilder.Sql(
                "ALTER TABLE \"Subscriptions\" ADD COLUMN IF NOT EXISTS \"StripeSubscriptionId\" character varying(255);");
            migrationBuilder.Sql(
                "ALTER TABLE \"Households\" ADD COLUMN IF NOT EXISTS \"StripeCustomerId\" character varying(255);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StripeSubscriptionId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "Households");
        }
    }
}
