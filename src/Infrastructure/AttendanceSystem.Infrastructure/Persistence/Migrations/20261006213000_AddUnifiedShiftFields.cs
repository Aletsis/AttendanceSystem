using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnifiedShiftFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PunchTrackingMode",
                table: "Shifts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasEntryWindow",
                table: "Shifts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "PunchTrackingMode",
                table: "DailyAttendances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasEntryWindow",
                table: "DailyAttendances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TotalWorkedMinutes",
                table: "DailyAttendances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IntervalsData",
                table: "DailyAttendances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OvertimeCalculationMethod",
                table: "DailyAttendances",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PunchTrackingMode",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "HasEntryWindow",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "PunchTrackingMode",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "HasEntryWindow",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "TotalWorkedMinutes",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "IntervalsData",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "OvertimeCalculationMethod",
                table: "DailyAttendances");
        }
    }
}
