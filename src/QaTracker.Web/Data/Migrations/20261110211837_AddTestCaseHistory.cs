using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTestCaseHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestCaseEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: true),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FromValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ToValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestCaseEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestCaseEvents_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TestCaseEvents_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestCaseEvents_ActorId",
                table: "TestCaseEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_TestCaseEvents_TestCaseId",
                table: "TestCaseEvents",
                column: "TestCaseId");

            // Backfill what existing rows already tell us, so cases created before history was
            // recorded don't show an empty timeline: their creation, the files they hold, and
            // the current result (who set it and when — the previous result isn't known, so
            // FromValue stays null and the timeline says "marked it as …").
            migrationBuilder.Sql("""
                INSERT INTO "TestCaseEvents" ("Id", "TestCaseId", "Kind", "ActorId", "OccurredUtc")
                SELECT gen_random_uuid(), tc."Id", 'Created', tc."CreatedById", tc."CreatedUtc"
                FROM "TestCases" tc;

                INSERT INTO "TestCaseEvents" ("Id", "TestCaseId", "Kind", "ActorId", "OccurredUtc", "ToValue")
                SELECT gen_random_uuid(), a."TestCaseId", 'FileAdded', a."UploadedById", a."UploadedUtc", LEFT(a."FileName", 500)
                FROM "Attachments" a
                WHERE a."TestCaseId" IS NOT NULL;

                INSERT INTO "TestCaseEvents" ("Id", "TestCaseId", "Kind", "ActorId", "OccurredUtc", "ToValue")
                SELECT gen_random_uuid(), tc."Id", 'ResultChanged', tc."TestedById", tc."TestedUtc", tc."Result"
                FROM "TestCases" tc
                WHERE tc."Result" <> 'NotRun' AND tc."TestedUtc" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestCaseEvents");
        }
    }
}
