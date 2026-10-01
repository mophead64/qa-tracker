using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTestCaseTestedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TestedById",
                table: "TestCases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TestedUtc",
                table: "TestCases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_TestedById",
                table: "TestCases",
                column: "TestedById");

            migrationBuilder.AddForeignKey(
                name: "FK_TestCases_AspNetUsers_TestedById",
                table: "TestCases",
                column: "TestedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestCases_AspNetUsers_TestedById",
                table: "TestCases");

            migrationBuilder.DropIndex(
                name: "IX_TestCases_TestedById",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "TestedById",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "TestedUtc",
                table: "TestCases");
        }
    }
}
