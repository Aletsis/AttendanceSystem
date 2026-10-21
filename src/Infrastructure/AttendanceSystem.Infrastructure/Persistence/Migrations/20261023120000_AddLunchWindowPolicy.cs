using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Regla de comida por ventana global: ventana y minutos con goce en la configuración, minutos con goce
    /// opcionales por turno, duración y exceso de comida por jornada, y perdón del exceso como decisión auditada.
    /// <c>Shifts.LunchBreakMinutes</c> se conserva como dato heredado y ya no interviene en el cálculo. Las jornadas
    /// existentes conservan lo descontado hasta su siguiente recálculo.
    /// </summary>
    public partial class AddLunchWindowPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(name: "LunchWindowStart", table: "SystemConfiguration", type: "interval", nullable: false, defaultValue: new TimeSpan(12, 30, 0));
            migrationBuilder.AddColumn<TimeSpan>(name: "LunchWindowEnd", table: "SystemConfiguration", type: "interval", nullable: false, defaultValue: new TimeSpan(15, 0, 0));
            migrationBuilder.AddColumn<int>(name: "PaidLunchMinutes", table: "SystemConfiguration", type: "integer", nullable: false, defaultValue: 30);

            migrationBuilder.AddColumn<int>(name: "PaidLunchMinutes", table: "Shifts", type: "integer", nullable: true);

            migrationBuilder.AddColumn<int>(name: "LunchMinutesTaken", table: "DailyAttendances", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "LunchExcessMinutes", table: "DailyAttendances", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<bool>(name: "LunchExcessForgiven", table: "DailyAttendances", type: "boolean", nullable: false, defaultValue: false);

            migrationBuilder.AddColumn<bool>(name: "LunchExcessForgiven", table: "AttendanceAdjustments", type: "boolean", nullable: false, defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LunchExcessForgiven", table: "AttendanceAdjustments");
            migrationBuilder.DropColumn(name: "LunchExcessForgiven", table: "DailyAttendances");
            migrationBuilder.DropColumn(name: "LunchExcessMinutes", table: "DailyAttendances");
            migrationBuilder.DropColumn(name: "LunchMinutesTaken", table: "DailyAttendances");
            migrationBuilder.DropColumn(name: "PaidLunchMinutes", table: "Shifts");
            migrationBuilder.DropColumn(name: "PaidLunchMinutes", table: "SystemConfiguration");
            migrationBuilder.DropColumn(name: "LunchWindowEnd", table: "SystemConfiguration");
            migrationBuilder.DropColumn(name: "LunchWindowStart", table: "SystemConfiguration");
        }
    }
}
