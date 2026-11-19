using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDevelopmentBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoardMode",
                table: "Projects",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                // Existing projects start without a board.
                defaultValue: "Off");

            migrationBuilder.AddColumn<Guid>(
                name: "BoardItemId",
                table: "Notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BoardItemCommentId",
                table: "CommentReactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BoardItemCommentId",
                table: "Attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BoardItemId",
                table: "Attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BoardItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    Template = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ExpectedResults = table.Column<string>(type: "text", nullable: true),
                    ActualResults = table.Column<string>(type: "text", nullable: true),
                    Column = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Done = table.Column<bool>(type: "boolean", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    AssignedToId = table.Column<string>(type: "text", nullable: true),
                    CreatedById = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExternalProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExternalUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoardItems_AspNetUsers_AssignedToId",
                        column: x => x.AssignedToId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BoardItems_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BoardItems_BoardItems_ParentId",
                        column: x => x.ParentId,
                        principalTable: "BoardItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BoardItems_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BoardItemComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardItemComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoardItemComments_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BoardItemComments_BoardItems_BoardItemId",
                        column: x => x.BoardItemId,
                        principalTable: "BoardItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BoardItemEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: true),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FromValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ToValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardItemEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoardItemEvents_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BoardItemEvents_BoardItems_BoardItemId",
                        column: x => x.BoardItemId,
                        principalTable: "BoardItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BoardItemLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TestCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefectId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardItemLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoardItemLinks_BoardItems_BoardItemId",
                        column: x => x.BoardItemId,
                        principalTable: "BoardItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BoardItemLinks_Defects_DefectId",
                        column: x => x.DefectId,
                        principalTable: "Defects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BoardItemLinks_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BoardItemLinks_TestScopes_TestScopeId",
                        column: x => x.TestScopeId,
                        principalTable: "TestScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_BoardItemId",
                table: "Notifications",
                column: "BoardItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentReactions_BoardItemCommentId_UserId",
                table: "CommentReactions",
                columns: new[] { "BoardItemCommentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_BoardItemCommentId",
                table: "Attachments",
                column: "BoardItemCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_BoardItemId",
                table: "Attachments",
                column: "BoardItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItemComments_AuthorId",
                table: "BoardItemComments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItemComments_BoardItemId",
                table: "BoardItemComments",
                column: "BoardItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItemEvents_ActorId",
                table: "BoardItemEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItemEvents_BoardItemId",
                table: "BoardItemEvents",
                column: "BoardItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItemLinks_BoardItemId_DefectId",
                table: "BoardItemLinks",
                columns: new[] { "BoardItemId", "DefectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardItemLinks_BoardItemId_TestCaseId",
                table: "BoardItemLinks",
                columns: new[] { "BoardItemId", "TestCaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardItemLinks_BoardItemId_TestScopeId",
                table: "BoardItemLinks",
                columns: new[] { "BoardItemId", "TestScopeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoardItemLinks_DefectId",
                table: "BoardItemLinks",
                column: "DefectId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItemLinks_TestCaseId",
                table: "BoardItemLinks",
                column: "TestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItemLinks_TestScopeId",
                table: "BoardItemLinks",
                column: "TestScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItems_AssignedToId",
                table: "BoardItems",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItems_CreatedById",
                table: "BoardItems",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItems_ParentId",
                table: "BoardItems",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardItems_ProjectId_Type_Number",
                table: "BoardItems",
                columns: new[] { "ProjectId", "Type", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_BoardItemComments_BoardItemCommentId",
                table: "Attachments",
                column: "BoardItemCommentId",
                principalTable: "BoardItemComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_BoardItems_BoardItemId",
                table: "Attachments",
                column: "BoardItemId",
                principalTable: "BoardItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CommentReactions_BoardItemComments_BoardItemCommentId",
                table: "CommentReactions",
                column: "BoardItemCommentId",
                principalTable: "BoardItemComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_BoardItems_BoardItemId",
                table: "Notifications",
                column: "BoardItemId",
                principalTable: "BoardItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_BoardItemComments_BoardItemCommentId",
                table: "Attachments");

            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_BoardItems_BoardItemId",
                table: "Attachments");

            migrationBuilder.DropForeignKey(
                name: "FK_CommentReactions_BoardItemComments_BoardItemCommentId",
                table: "CommentReactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_BoardItems_BoardItemId",
                table: "Notifications");

            migrationBuilder.DropTable(
                name: "BoardItemComments");

            migrationBuilder.DropTable(
                name: "BoardItemEvents");

            migrationBuilder.DropTable(
                name: "BoardItemLinks");

            migrationBuilder.DropTable(
                name: "BoardItems");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_BoardItemId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_CommentReactions_BoardItemCommentId_UserId",
                table: "CommentReactions");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_BoardItemCommentId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_BoardItemId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "BoardMode",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "BoardItemId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "BoardItemCommentId",
                table: "CommentReactions");

            migrationBuilder.DropColumn(
                name: "BoardItemCommentId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "BoardItemId",
                table: "Attachments");
        }
    }
}
