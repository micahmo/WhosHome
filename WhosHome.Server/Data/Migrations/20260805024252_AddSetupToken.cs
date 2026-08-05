using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhosHome.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSetupToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SetupToken",
                table: "People",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SetupTokenExpiresUtc",
                table: "People",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_SetupToken",
                table: "People",
                column: "SetupToken");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_People_SetupToken",
                table: "People");

            migrationBuilder.DropColumn(
                name: "SetupToken",
                table: "People");

            migrationBuilder.DropColumn(
                name: "SetupTokenExpiresUtc",
                table: "People");
        }
    }
}
