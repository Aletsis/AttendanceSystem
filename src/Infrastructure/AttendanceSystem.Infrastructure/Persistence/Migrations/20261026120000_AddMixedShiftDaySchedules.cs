using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Turno mixto como plantilla semanal: cada día referencia un horario existente que rige el cálculo completo.
    /// Los días existentes (horas por día) quedan con <c>DayShiftId</c> nulo: se siguen calculando como antes, con
    /// una incidencia visible, hasta que RH les asigne un horario. No se crean horarios automáticamente.
    /// </summary>
    public partial class AddMixedShiftDaySchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(name: "DayShiftId", table: "ShiftDays", type: "uuid", nullable: true);
            migrationBuilder.CreateIndex(name: "IX_ShiftDays_DayShiftId", table: "ShiftDays", column: "DayShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_ShiftDays_DayShiftId", table: "ShiftDays");
            migrationBuilder.DropColumn(name: "DayShiftId", table: "ShiftDays");
        }
    }
}
