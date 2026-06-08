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
            migrationBuilder.Sql("ALTER TABLE \"DailyAttendances\" ADD COLUMN IF NOT EXISTS \"OvertimeAuthorized\" boolean NOT NULL DEFAULT false;");
            migrationBuilder.Sql("ALTER TABLE \"DailyAttendances\" ADD COLUMN IF NOT EXISTS \"CalculateOvertimeBeforeEntry\" boolean NOT NULL DEFAULT false;");
            migrationBuilder.Sql("ALTER TABLE \"DailyAttendances\" ADD COLUMN IF NOT EXISTS \"ShiftType\" integer NULL;");
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
