using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefectCommentId",
                table: "Attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TestCaseCommentId",
                table: "Attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_DefectCommentId",
                table: "Attachments",
                column: "DefectCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_TestCaseCommentId",
                table: "Attachments",
                column: "TestCaseCommentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_DefectComments_DefectCommentId",
                table: "Attachments",
                column: "DefectCommentId",
                principalTable: "DefectComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_TestCaseComments_TestCaseCommentId",
                table: "Attachments",
                column: "TestCaseCommentId",
                principalTable: "TestCaseComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_DefectComments_DefectCommentId",
                table: "Attachments");

            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_TestCaseComments_TestCaseCommentId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_DefectCommentId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_TestCaseCommentId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "DefectCommentId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "TestCaseCommentId",
                table: "Attachments");
        }
    }
}
