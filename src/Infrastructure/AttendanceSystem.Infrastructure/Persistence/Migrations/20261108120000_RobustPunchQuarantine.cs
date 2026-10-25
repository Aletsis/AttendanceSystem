using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Caso 18: umbrales configurables de la cuarentena, fecha mínima de marcajes por reloj, la regla y la corrección
    /// sugerida de cada marcaje en cuarentena, y los periodos en que un reloj estuvo atrasado.
    /// </summary>
    public partial class RobustPunchQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "QuarantineMinDateDaysBeforeDevice", table: "SystemConfiguration", type: "integer", nullable: false, defaultValue: 30);
            migrationBuilder.AddColumn<int>(name: "QuarantineFutureToleranceMinutes", table: "SystemConfiguration", type: "integer", nullable: false, defaultValue: 5);
            migrationBuilder.AddColumn<int>(name: "QuarantineBackwardJumpMinutes", table: "SystemConfiguration", type: "integer", nullable: false, defaultValue: 60);
            migrationBuilder.AddColumn<int>(name: "QuarantineRealtimeLagMinutes", table: "SystemConfiguration", type: "integer", nullable: false, defaultValue: 30);
            migrationBuilder.AddColumn<int>(name: "QuarantineClockDriftAlertMinutes", table: "SystemConfiguration", type: "integer", nullable: false, defaultValue: 10);
            migrationBuilder.AddColumn<int>(name: "QuarantineSdkReadMarginDays", table: "SystemConfiguration", type: "integer", nullable: false, defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(name: "AcceptPunchesFrom", table: "Devices", type: "date", nullable: true);

            migrationBuilder.AddColumn<string>(name: "Rule", table: "QuarantinedPunches", type: "character varying(60)", maxLength: 60, nullable: true);
            migrationBuilder.AddColumn<int>(name: "SuggestedOffsetMinutes", table: "QuarantinedPunches", type: "integer", nullable: true);

            migrationBuilder.CreateTable(
                name: "DeviceClockIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReportedFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReportedTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OffsetMinutes = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_DeviceClockIncidents", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_DeviceClockIncidents_DeviceId", table: "DeviceClockIncidents", column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DeviceClockIncidents");
            migrationBuilder.DropColumn(name: "SuggestedOffsetMinutes", table: "QuarantinedPunches");
            migrationBuilder.DropColumn(name: "Rule", table: "QuarantinedPunches");
            migrationBuilder.DropColumn(name: "AcceptPunchesFrom", table: "Devices");
            migrationBuilder.DropColumn(name: "QuarantineSdkReadMarginDays", table: "SystemConfiguration");
            migrationBuilder.DropColumn(name: "QuarantineClockDriftAlertMinutes", table: "SystemConfiguration");
            migrationBuilder.DropColumn(name: "QuarantineRealtimeLagMinutes", table: "SystemConfiguration");
            migrationBuilder.DropColumn(name: "QuarantineBackwardJumpMinutes", table: "SystemConfiguration");
            migrationBuilder.DropColumn(name: "QuarantineFutureToleranceMinutes", table: "SystemConfiguration");
            migrationBuilder.DropColumn(name: "QuarantineMinDateDaysBeforeDevice", table: "SystemConfiguration");
        }
    }
}
