using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""Shifts"" ADD COLUMN IF NOT EXISTS ""HasEntryWindow"" boolean NOT NULL DEFAULT TRUE;
                ALTER TABLE ""Shifts"" ADD COLUMN IF NOT EXISTS ""PunchTrackingMode"" integer NOT NULL DEFAULT 0;

                ALTER TABLE ""Employees"" ADD COLUMN IF NOT EXISTS ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE ""Employees"" ADD COLUMN IF NOT EXISTS ""CreatedBy"" character varying(256) NULL;
                ALTER TABLE ""Employees"" ADD COLUMN IF NOT EXISTS ""RestDays"" character varying(50) NULL;
                ALTER TABLE ""Employees"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" timestamp with time zone NULL;
                ALTER TABLE ""Employees"" ADD COLUMN IF NOT EXISTS ""UpdatedBy"" character varying(256) NULL;

                ALTER TABLE ""DailyAttendances"" ADD COLUMN IF NOT EXISTS ""HasEntryWindow"" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE ""DailyAttendances"" ADD COLUMN IF NOT EXISTS ""IntervalsData"" text NULL;
                ALTER TABLE ""DailyAttendances"" ADD COLUMN IF NOT EXISTS ""OvertimeCalculationMethod"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""DailyAttendances"" ADD COLUMN IF NOT EXISTS ""PunchTrackingMode"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""DailyAttendances"" ADD COLUMN IF NOT EXISTS ""TotalWorkedMinutes"" integer NOT NULL DEFAULT 0;

                CREATE TABLE IF NOT EXISTS ""EmployeeAuditLogs"" (
                    ""Id"" uuid NOT NULL,
                    ""EmployeeId"" character varying(20) NOT NULL,
                    ""Action"" character varying(100) NOT NULL,
                    ""Timestamp"" timestamp with time zone NOT NULL,
                    ""UserId"" character varying(450) NULL,
                    ""UserName"" character varying(256) NULL,
                    ""Details"" character varying(1000) NULL,
                    ""ChangesJson"" text NULL,
                    CONSTRAINT ""PK_EmployeeAuditLogs"" PRIMARY KEY (""Id"")
                );

                CREATE INDEX IF NOT EXISTS ""IX_EmployeeAuditLogs_EmployeeId"" ON ""EmployeeAuditLogs"" (""EmployeeId"");
                CREATE INDEX IF NOT EXISTS ""IX_EmployeeAuditLogs_EmployeeId_Timestamp"" ON ""EmployeeAuditLogs"" (""EmployeeId"", ""Timestamp"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeAuditLogs");

            migrationBuilder.DropColumn(
                name: "HasEntryWindow",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "PunchTrackingMode",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "RestDays",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "HasEntryWindow",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "IntervalsData",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "OvertimeCalculationMethod",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "PunchTrackingMode",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "TotalWorkedMinutes",
                table: "DailyAttendances");
        }
    }
}
