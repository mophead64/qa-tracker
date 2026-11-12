using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: true),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ToValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectEvents_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProjectEvents_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestCaseEvents_OccurredUtc",
                table: "TestCaseEvents",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DefectEvents_OccurredUtc",
                table: "DefectEvents",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvents_ActorId",
                table: "ProjectEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEvents_ProjectId_OccurredUtc",
                table: "ProjectEvents",
                columns: new[] { "ProjectId", "OccurredUtc" });

            // Backfill what the database already knows: each project's and scope's creation and
            // the project files on record. Past status / team / edit changes were never stored.
            // Actors are looked up so an id with no user left becomes null, as SetNull would.
            migrationBuilder.Sql("""
                INSERT INTO "ProjectEvents" ("Id", "ProjectId", "Kind", "ActorId", "OccurredUtc")
                SELECT gen_random_uuid(), p."Id", 'Created',
                       (SELECT u."Id" FROM "AspNetUsers" u WHERE u."Id" = p."CreatedById"), p."CreatedUtc"
                FROM "Projects" p;

                INSERT INTO "ProjectEvents" ("Id", "ProjectId", "Kind", "ActorId", "OccurredUtc", "SubjectId", "ToValue")
                SELECT gen_random_uuid(), s."ProjectId", 'ScopeCreated',
                       (SELECT u."Id" FROM "AspNetUsers" u WHERE u."Id" = s."CreatedById"), s."CreatedUtc", s."Id", LEFT(s."Name", 500)
                FROM "TestScopes" s;

                INSERT INTO "ProjectEvents" ("Id", "ProjectId", "Kind", "ActorId", "OccurredUtc", "ToValue")
                SELECT gen_random_uuid(), a."ProjectId", 'FileAdded',
                       (SELECT u."Id" FROM "AspNetUsers" u WHERE u."Id" = a."UploadedById"), a."UploadedUtc", LEFT(a."FileName", 500)
                FROM "Attachments" a
                WHERE a."ProjectId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectEvents");

            migrationBuilder.DropIndex(
                name: "IX_TestCaseEvents_OccurredUtc",
                table: "TestCaseEvents");

            migrationBuilder.DropIndex(
                name: "IX_DefectEvents_OccurredUtc",
                table: "DefectEvents");
        }
    }
}
