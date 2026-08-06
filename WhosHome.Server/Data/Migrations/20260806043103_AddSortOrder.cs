using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhosHome.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "People",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Seed from Id so an existing household keeps the order it already has. Without this
            // every row sits at 0 and the list falls back to the Id tiebreak, which happens to
            // look right today and would stop looking right the moment anyone is reordered.
            migrationBuilder.Sql("UPDATE People SET SortOrder = Id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "People");
        }
    }
}
