using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTemporaryExitsAndLunchBreak : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LunchBreakMinutes",
                table: "Shifts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasTemporaryExits",
                table: "DailyAttendances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LunchBreakMinutesApplied",
                table: "DailyAttendances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TemporaryExitMinutes",
                table: "DailyAttendances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TemporaryExitNote",
                table: "DailyAttendances",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TemporaryExitStatus",
                table: "DailyAttendances",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LunchBreakMinutes",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "HasTemporaryExits",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "LunchBreakMinutesApplied",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "TemporaryExitMinutes",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "TemporaryExitNote",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "TemporaryExitStatus",
                table: "DailyAttendances");
        }
    }
}
