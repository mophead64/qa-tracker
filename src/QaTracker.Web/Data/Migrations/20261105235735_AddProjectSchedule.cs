using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QaTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultQaEffortPercent",
                table: "SystemSettings",
                type: "integer",
                nullable: false,
                // Hand-set (EF scaffolds 0): the settings row starts at QaEffort.DefaultPercent
                // (25%), the same value the UpdateData seed below writes.
                defaultValue: 25);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedDevDays",
                table: "Projects",
                type: "numeric(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedQaDays",
                table: "Projects",
                type: "numeric(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QaEffortPercent",
                table: "Projects",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TestingEndDate",
                table: "Projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TestingEndsMidday",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TestingStartDate",
                table: "Projects",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TestingStartsMidday",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultQaEffortPercent",
                value: 25);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultQaEffortPercent",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "EstimatedDevDays",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "EstimatedQaDays",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "QaEffortPercent",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TestingEndDate",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TestingEndsMidday",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TestingStartDate",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TestingStartsMidday",
                table: "Projects");
        }
    }
}
