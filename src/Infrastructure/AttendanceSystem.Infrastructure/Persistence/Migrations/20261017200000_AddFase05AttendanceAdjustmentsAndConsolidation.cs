using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFase05AttendanceAdjustmentsAndConsolidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasManualAdjustments",
                table: "DailyAttendances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRestDayOverridden",
                table: "DailyAttendances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RawOvertimeMinutes",
                table: "DailyAttendances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TerminationDate",
                table: "Employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    IsRestDayOverride = table.Column<bool>(type: "boolean", nullable: true),
                    ShiftIdOverride = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckInRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckInTimeOverride = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckOutRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckOutTimeOverride = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Block1CheckOutRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    Block1CheckOutTimeOverride = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Block2CheckInRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    Block2CheckInTimeOverride = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TemporaryExitStatus = table.Column<int>(type: "integer", nullable: true),
                    TemporaryExitNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OvertimeAuthorizedOverride = table.Column<bool>(type: "boolean", nullable: true),
                    CalculateOvertimeBeforeEntryOverride = table.Column<bool>(type: "boolean", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceAdjustments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceConsolidationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceConsolidationRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAdjustments_Date",
                table: "AttendanceAdjustments",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAdjustments_EmployeeId_Date",
                table: "AttendanceAdjustments",
                columns: new[] { "EmployeeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConsolidationRequests_EmployeeId_Status",
                table: "AttendanceConsolidationRequests",
                columns: new[] { "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConsolidationRequests_Status_RequestedAt",
                table: "AttendanceConsolidationRequests",
                columns: new[] { "Status", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceAdjustments");

            migrationBuilder.DropTable(
                name: "AttendanceConsolidationRequests");

            migrationBuilder.DropColumn(
                name: "TerminationDate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "RawOvertimeMinutes",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "IsRestDayOverridden",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "HasManualAdjustments",
                table: "DailyAttendances");
        }
    }
}
