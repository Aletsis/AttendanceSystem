using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Caso 14: los marcajes son inmutables. Uno equivocado se anula (autor, motivo y, si se corrigió, el marcaje que lo
    /// reemplaza) y la identidad (empleado, instante) es única sólo entre los vigentes. El toque doble es configurable.
    /// </summary>
    public partial class ImmutablePunches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(name: "VoidedAt", table: "AttendanceRecords", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<string>(name: "VoidedBy", table: "AttendanceRecords", type: "character varying(256)", maxLength: 256, nullable: true);
            migrationBuilder.AddColumn<string>(name: "VoidReason", table: "AttendanceRecords", type: "character varying(500)", maxLength: 500, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "ReplacedByRecordId", table: "AttendanceRecords", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "ReplacesRecordId", table: "AttendanceRecords", type: "uuid", nullable: true);

            migrationBuilder.DropIndex(name: "IX_AttendanceRecords_EmployeeId_CheckTime", table: "AttendanceRecords");
            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_EmployeeId_CheckTime",
                table: "AttendanceRecords",
                columns: new[] { "EmployeeId", "CheckTime" },
                unique: true,
                filter: "\"VoidedAt\" IS NULL");

            migrationBuilder.AddColumn<Guid>(name: "NewRecordId", table: "PunchTimeCorrectionEntries", type: "uuid", nullable: true);

            migrationBuilder.AddColumn<int>(name: "DoubleTapMinutes", table: "SystemConfiguration", type: "integer", nullable: false, defaultValue: 15);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DoubleTapMinutes", table: "SystemConfiguration");
            migrationBuilder.DropColumn(name: "NewRecordId", table: "PunchTimeCorrectionEntries");

            migrationBuilder.DropIndex(name: "IX_AttendanceRecords_EmployeeId_CheckTime", table: "AttendanceRecords");
            migrationBuilder.Sql("DELETE FROM \"AttendanceRecords\" WHERE \"VoidedAt\" IS NOT NULL;");
            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_EmployeeId_CheckTime",
                table: "AttendanceRecords",
                columns: new[] { "EmployeeId", "CheckTime" },
                unique: true);

            migrationBuilder.DropColumn(name: "VoidedAt", table: "AttendanceRecords");
            migrationBuilder.DropColumn(name: "VoidedBy", table: "AttendanceRecords");
            migrationBuilder.DropColumn(name: "VoidReason", table: "AttendanceRecords");
            migrationBuilder.DropColumn(name: "ReplacedByRecordId", table: "AttendanceRecords");
            migrationBuilder.DropColumn(name: "ReplacesRecordId", table: "AttendanceRecords");
        }
    }
}
