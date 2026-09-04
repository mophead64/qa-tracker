using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DefectTestCaseManyToMany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DefectTestCases",
                columns: table => new
                {
                    DefectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestCasesId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefectTestCases", x => new { x.DefectId, x.TestCasesId });
                    table.ForeignKey(
                        name: "FK_DefectTestCases_Defects_DefectId",
                        column: x => x.DefectId,
                        principalTable: "Defects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DefectTestCases_TestCases_TestCasesId",
                        column: x => x.TestCasesId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DefectTestCases_TestCasesId",
                table: "DefectTestCases",
                column: "TestCasesId");

            // Carry existing single links over to the join table before dropping the column.
            migrationBuilder.Sql(
                """
                INSERT INTO "DefectTestCases" ("DefectId", "TestCasesId")
                SELECT "Id", "TestCaseId" FROM "Defects" WHERE "TestCaseId" IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Defects_TestCases_TestCaseId",
                table: "Defects");

            migrationBuilder.DropIndex(
                name: "IX_Defects_TestCaseId",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "TestCaseId",
                table: "Defects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TestCaseId",
                table: "Defects",
                type: "uuid",
                nullable: true);

            // Best effort: keep one link per defect.
            migrationBuilder.Sql(
                """
                UPDATE "Defects" d
                SET "TestCaseId" = (
                    SELECT "TestCasesId" FROM "DefectTestCases" j
                    WHERE j."DefectId" = d."Id"
                    ORDER BY "TestCasesId"
                    LIMIT 1);
                """);

            migrationBuilder.DropTable(
                name: "DefectTestCases");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_TestCaseId",
                table: "Defects",
                column: "TestCaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Defects_TestCases_TestCaseId",
                table: "Defects",
                column: "TestCaseId",
                principalTable: "TestCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
