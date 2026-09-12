using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSoundPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotificationSound",
                table: "AspNetUsers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "fah");

            migrationBuilder.AddColumn<bool>(
                name: "NotificationSoundEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotificationSound",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotificationSoundEnabled",
                table: "AspNetUsers");
        }
    }
}
