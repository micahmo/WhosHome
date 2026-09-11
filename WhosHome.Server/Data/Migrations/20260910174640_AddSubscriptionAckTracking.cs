using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhosHome.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionAckTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastAckUtc",
                table: "Subscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LastPushUtc",
                table: "Subscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Existing subscriptions start with their liveness clocks level at creation time, so a
            // row that predates this feature does not look overdue for acknowledgement. CreatedUtc is
            // the same packed long, so it copies straight across.
            migrationBuilder.Sql(
                "UPDATE \"Subscriptions\" SET \"LastPushUtc\" = \"CreatedUtc\", \"LastAckUtc\" = \"CreatedUtc\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastAckUtc",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "LastPushUtc",
                table: "Subscriptions");
        }
    }
}
