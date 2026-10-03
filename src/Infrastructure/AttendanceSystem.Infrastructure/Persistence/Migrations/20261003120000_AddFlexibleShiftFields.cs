using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlexibleShiftFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "FlexWindowEndTime",
                table: "Shifts",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "WeeklyWorkHours",
                table: "Shifts",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "FlexWindowEndTime",
                table: "DailyAttendances",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "WorkHours",
                table: "DailyAttendances",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DynamicScheduledCheckOut",
                table: "DailyAttendances",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FlexWindowEndTime",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "WeeklyWorkHours",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "FlexWindowEndTime",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "WorkHours",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "DynamicScheduledCheckOut",
                table: "DailyAttendances");
        }
    }
}
