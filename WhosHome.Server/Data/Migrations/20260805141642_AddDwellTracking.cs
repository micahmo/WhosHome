using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhosHome.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDwellTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MovedMeters",
                table: "Reports",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StationarySinceUtc",
                table: "People",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MovedMeters",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "StationarySinceUtc",
                table: "People");
        }
    }
}
