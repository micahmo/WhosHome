using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhosHome.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportSpeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SpeedMetersPerSecond",
                table: "Reports",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpeedMetersPerSecond",
                table: "Reports");
        }
    }
}
