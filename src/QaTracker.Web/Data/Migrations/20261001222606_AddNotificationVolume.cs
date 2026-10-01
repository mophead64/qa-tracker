using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationVolume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NotificationVolume",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                // Hand-set (EF scaffolds 0): existing users start at 50%, the same as
                // NotificationSounds.DefaultVolume, which new accounts get from
                // ApplicationUser.NotificationVolume's initializer.
                defaultValue: 50);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotificationVolume",
                table: "AspNetUsers");
        }
    }
}
