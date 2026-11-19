using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClosedById",
                table: "BoardItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DevelopedById",
                table: "BoardItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QaById",
                table: "BoardItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestedById",
                table: "BoardItems",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardItems_ClosedById",
                table: "BoardItems",
                column: "ClosedById");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItems_DevelopedById",
                table: "BoardItems",
                column: "DevelopedById");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItems_QaById",
                table: "BoardItems",
                column: "QaById");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItems_TestedById",
                table: "BoardItems",
                column: "TestedById");

            migrationBuilder.AddForeignKey(
                name: "FK_BoardItems_AspNetUsers_ClosedById",
                table: "BoardItems",
                column: "ClosedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_BoardItems_AspNetUsers_DevelopedById",
                table: "BoardItems",
                column: "DevelopedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_BoardItems_AspNetUsers_QaById",
                table: "BoardItems",
                column: "QaById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_BoardItems_AspNetUsers_TestedById",
                table: "BoardItems",
                column: "TestedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BoardItems_AspNetUsers_ClosedById",
                table: "BoardItems");

            migrationBuilder.DropForeignKey(
                name: "FK_BoardItems_AspNetUsers_DevelopedById",
                table: "BoardItems");

            migrationBuilder.DropForeignKey(
                name: "FK_BoardItems_AspNetUsers_QaById",
                table: "BoardItems");

            migrationBuilder.DropForeignKey(
                name: "FK_BoardItems_AspNetUsers_TestedById",
                table: "BoardItems");

            migrationBuilder.DropIndex(
                name: "IX_BoardItems_ClosedById",
                table: "BoardItems");

            migrationBuilder.DropIndex(
                name: "IX_BoardItems_DevelopedById",
                table: "BoardItems");

            migrationBuilder.DropIndex(
                name: "IX_BoardItems_QaById",
                table: "BoardItems");

            migrationBuilder.DropIndex(
                name: "IX_BoardItems_TestedById",
                table: "BoardItems");

            migrationBuilder.DropColumn(
                name: "ClosedById",
                table: "BoardItems");

            migrationBuilder.DropColumn(
                name: "DevelopedById",
                table: "BoardItems");

            migrationBuilder.DropColumn(
                name: "QaById",
                table: "BoardItems");

            migrationBuilder.DropColumn(
                name: "TestedById",
                table: "BoardItems");
        }
    }
}
