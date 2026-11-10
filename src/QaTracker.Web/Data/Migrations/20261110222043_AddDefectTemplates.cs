using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDefectTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultDefectTemplate",
                table: "SystemSettings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Advanced"); // existing rows keep the all-fields layout

            migrationBuilder.AddColumn<string>(
                name: "DefaultDefectTemplate",
                table: "Projects",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Template",
                table: "Defects",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Advanced"); // existing rows keep the all-fields layout

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultDefectTemplate",
                value: "Advanced");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultDefectTemplate",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "DefaultDefectTemplate",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Template",
                table: "Defects");
        }
    }
}
