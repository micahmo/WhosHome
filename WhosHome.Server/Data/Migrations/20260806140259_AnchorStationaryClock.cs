using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhosHome.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnchorStationaryClock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastFixUtc",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StationaryLatitude",
                table: "People",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StationaryLongitude",
                table: "People",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastFixUtc",
                table: "People");

            migrationBuilder.DropColumn(
                name: "StationaryLatitude",
                table: "People");

            migrationBuilder.DropColumn(
                name: "StationaryLongitude",
                table: "People");
        }
    }
}
