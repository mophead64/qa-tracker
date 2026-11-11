using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDefectHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DefectEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: true),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FromValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ToValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefectEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DefectEvents_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DefectEvents_Defects_DefectId",
                        column: x => x.DefectId,
                        principalTable: "Defects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DefectEvents_ActorId",
                table: "DefectEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectEvents_DefectId",
                table: "DefectEvents",
                column: "DefectId");

            // Backfill what existing rows already record: each defect's creation (and who raised
            // it) and the files it holds. Earlier status / assignee changes were never stored, so
            // the timeline of an existing defect starts with these.
            migrationBuilder.Sql("""
                INSERT INTO "DefectEvents" ("Id", "DefectId", "Kind", "ActorId", "OccurredUtc")
                SELECT gen_random_uuid(), d."Id", 'Created', d."CreatedById", d."CreatedUtc"
                FROM "Defects" d;

                INSERT INTO "DefectEvents" ("Id", "DefectId", "Kind", "ActorId", "OccurredUtc", "ToValue")
                SELECT gen_random_uuid(), a."DefectId", 'FileAdded', a."UploadedById", a."UploadedUtc", LEFT(a."FileName", 500)
                FROM "Attachments" a
                WHERE a."DefectId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DefectEvents");
        }
    }
}
