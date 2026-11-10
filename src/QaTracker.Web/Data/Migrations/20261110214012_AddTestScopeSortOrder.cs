using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTestScopeSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "TestScopes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing scopes start in the order they were created, per project (0, 1, 2, …).
            migrationBuilder.Sql("""
                UPDATE "TestScopes" s
                SET "SortOrder" = o.rn
                FROM (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "ProjectId" ORDER BY "CreatedUtc", "Id") - 1 AS rn
                    FROM "TestScopes"
                ) o
                WHERE s."Id" = o."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "TestScopes");
        }
    }
}
