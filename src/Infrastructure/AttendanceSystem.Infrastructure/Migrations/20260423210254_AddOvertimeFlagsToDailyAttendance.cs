using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOvertimeFlagsToDailyAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OvertimeAuthorized",
                table: "DailyAttendances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ShiftType",
                table: "DailyAttendances",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OvertimeAuthorized",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "CalculateOvertimeBeforeEntry",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "ShiftType",
                table: "DailyAttendances");
        }
    }
}
