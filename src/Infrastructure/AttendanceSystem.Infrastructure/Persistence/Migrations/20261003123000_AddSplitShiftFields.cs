using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSplitShiftFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "SecondBlockStartTime",
                table: "Shifts",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "SecondBlockEndTime",
                table: "Shifts",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondBlockToleranceMinutes",
                table: "Shifts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "ScheduledBlock2CheckIn",
                table: "DailyAttendances",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "ScheduledBlock2CheckOut",
                table: "DailyAttendances",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecondBlockToleranceMinutes",
                table: "DailyAttendances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualBlock1CheckOut",
                table: "DailyAttendances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Block1CheckOutRecordId",
                table: "DailyAttendances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualBlock2CheckIn",
                table: "DailyAttendances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Block2CheckInRecordId",
                table: "DailyAttendances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MissingBlock1CheckOut",
                table: "DailyAttendances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MissingBlock2CheckIn",
                table: "DailyAttendances",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecondBlockStartTime",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "SecondBlockEndTime",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "SecondBlockToleranceMinutes",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ScheduledBlock2CheckIn",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "ScheduledBlock2CheckOut",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "SecondBlockToleranceMinutes",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "ActualBlock1CheckOut",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "Block1CheckOutRecordId",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "ActualBlock2CheckIn",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "Block2CheckInRecordId",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "MissingBlock1CheckOut",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "MissingBlock2CheckIn",
                table: "DailyAttendances");
        }
    }
}
