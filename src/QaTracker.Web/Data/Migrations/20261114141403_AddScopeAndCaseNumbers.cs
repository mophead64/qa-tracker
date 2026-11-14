using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScopeAndCaseNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "TestScopes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "TestCases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "TestCases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill before the unique indexes: each case's project from its scope, then S-/T-
            // numbers per project in the order the test cases page shows them (scope order, then
            // each scope's cases oldest first), so existing plans read S-1, S-2… and T-1, T-2… top
            // to bottom.
            migrationBuilder.Sql("""
                UPDATE "TestCases" AS tc
                SET "ProjectId" = s."ProjectId"
                FROM "TestScopes" AS s
                WHERE tc."TestScopeId" = s."Id";

                UPDATE "TestScopes" AS s
                SET "Number" = n.rn
                FROM (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "ProjectId" ORDER BY "SortOrder", "CreatedUtc", "Id") AS rn
                    FROM "TestScopes"
                ) AS n
                WHERE s."Id" = n."Id";

                UPDATE "TestCases" AS tc
                SET "Number" = n.rn
                FROM (
                    SELECT c."Id", ROW_NUMBER() OVER (
                        PARTITION BY c."ProjectId" ORDER BY s."SortOrder", s."CreatedUtc", c."CreatedUtc", c."Id") AS rn
                    FROM "TestCases" AS c
                    JOIN "TestScopes" AS s ON s."Id" = c."TestScopeId"
                ) AS n
                WHERE tc."Id" = n."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TestScopes_ProjectId_Number",
                table: "TestScopes",
                columns: new[] { "ProjectId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_ProjectId_Number",
                table: "TestCases",
                columns: new[] { "ProjectId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TestScopes_ProjectId_Number",
                table: "TestScopes");

            migrationBuilder.DropIndex(
                name: "IX_TestCases_ProjectId_Number",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "TestScopes");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "TestCases");
        }
    }
}
