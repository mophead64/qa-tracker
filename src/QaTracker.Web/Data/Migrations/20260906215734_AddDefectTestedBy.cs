using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDefectTestedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TestedById",
                table: "Defects",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Defects_TestedById",
                table: "Defects",
                column: "TestedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Defects_AspNetUsers_TestedById",
                table: "Defects",
                column: "TestedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Defects_AspNetUsers_TestedById",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_Defects_TestedById",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "TestedById",
                table: "Defects");
        }
    }
}
