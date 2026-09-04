using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class TestCaseDefectsNav : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DefectTestCases_Defects_DefectId",
                table: "DefectTestCases");

            migrationBuilder.RenameColumn(
                name: "DefectId",
                table: "DefectTestCases",
                newName: "DefectsId");

            migrationBuilder.AddForeignKey(
                name: "FK_DefectTestCases_Defects_DefectsId",
                table: "DefectTestCases",
                column: "DefectsId",
                principalTable: "Defects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DefectTestCases_Defects_DefectsId",
                table: "DefectTestCases");

            migrationBuilder.RenameColumn(
                name: "DefectsId",
                table: "DefectTestCases",
                newName: "DefectId");

            migrationBuilder.AddForeignKey(
                name: "FK_DefectTestCases_Defects_DefectId",
                table: "DefectTestCases",
                column: "DefectId",
                principalTable: "Defects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
