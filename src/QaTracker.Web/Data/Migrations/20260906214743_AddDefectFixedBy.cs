using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDefectFixedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FixedById",
                table: "Defects",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Defects_FixedById",
                table: "Defects",
                column: "FixedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Defects_AspNetUsers_FixedById",
                table: "Defects",
                column: "FixedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Defects_AspNetUsers_FixedById",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_Defects_FixedById",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "FixedById",
                table: "Defects");
        }
    }
}
