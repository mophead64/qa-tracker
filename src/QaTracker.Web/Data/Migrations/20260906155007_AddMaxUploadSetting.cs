using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxUploadSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue 20 (not 0) so the row seeded by AddSystemSettings — and any
            // existing production row — lands on the same limit as a fresh database.
            migrationBuilder.AddColumn<int>(
                name: "MaxUploadMb",
                table: "SystemSettings",
                type: "integer",
                nullable: false,
                defaultValue: 20);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxUploadMb",
                table: "SystemSettings");
        }
    }
}
