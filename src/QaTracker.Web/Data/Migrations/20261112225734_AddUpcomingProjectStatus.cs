using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUpcomingProjectStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // New statuses Upcoming and Cancelled need no schema change (Status is stored as its
            // name). Projects that already have testing dates but no work yet are Upcoming now.
            migrationBuilder.Sql("""
                UPDATE "Projects" SET "Status" = 'Upcoming'
                WHERE "Status" = 'NotStarted' AND "TestingStartDate" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old code only knows Not Started / Active / Completed.
            migrationBuilder.Sql("""
                UPDATE "Projects" SET "Status" = 'NotStarted' WHERE "Status" = 'Upcoming';
                UPDATE "Projects" SET "Status" = 'Complete' WHERE "Status" = 'Cancelled';
                """);
        }
    }
}
