using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameNotificationDismissedUtcToReadUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DismissedUtc",
                table: "Notifications",
                newName: "ReadUtc");

            migrationBuilder.RenameIndex(
                name: "IX_Notifications_UserId_DismissedUtc",
                table: "Notifications",
                newName: "IX_Notifications_UserId_ReadUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ReadUtc",
                table: "Notifications",
                newName: "DismissedUtc");

            migrationBuilder.RenameIndex(
                name: "IX_Notifications_UserId_ReadUtc",
                table: "Notifications",
                newName: "IX_Notifications_UserId_DismissedUtc");
        }
    }
}
