using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhosHome.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastState",
                table: "People",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubscriberPersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectPersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_People_SubjectPersonId",
                        column: x => x.SubjectPersonId,
                        principalTable: "People",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_People_SubscriberPersonId",
                        column: x => x.SubscriberPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    P256dh = table.Column<string>(type: "TEXT", nullable: false),
                    Auth = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_SubjectPersonId",
                table: "NotificationPreferences",
                column: "SubjectPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_SubscriberPersonId_SubjectPersonId",
                table: "NotificationPreferences",
                columns: new[] { "SubscriberPersonId", "SubjectPersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_Endpoint",
                table: "Subscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PersonId",
                table: "Subscriptions",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "LastState",
                table: "People");
        }
    }
}
