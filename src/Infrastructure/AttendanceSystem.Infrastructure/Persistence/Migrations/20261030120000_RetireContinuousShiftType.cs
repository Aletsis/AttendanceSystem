using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Retira el tipo de turno Continuo (4): lo reemplaza Flexible (5) sin ventana de llegada, con las mismas horas
    /// objetivo diarias. Como al crear un flexible sin ventana, la jornada se ancla a las 00:00.
    /// </summary>
    public partial class RetireContinuousShiftType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Shifts"
                SET "ShiftType" = 5,
                    "HasEntryWindow" = false,
                    "FlexWindowEndTime" = NULL,
                    "WeeklyWorkHours" = NULL,
                    "StartTime" = interval '0',
                    "EndTime" = make_interval(secs => mod(extract(epoch FROM "WorkHours"), 86400)::double precision)
                WHERE "ShiftType" = 4;

                UPDATE "ShiftDays" SET "ShiftType" = 5 WHERE "ShiftType" = 4;
                UPDATE "Employees" SET "ShiftType" = 5 WHERE "ShiftType" = 4;
                UPDATE "EmployeeScheduleAssignments" SET "ShiftType" = 5 WHERE "ShiftType" = 4;
                UPDATE "DailyAttendances" SET "ShiftType" = 5 WHERE "ShiftType" = 4;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible: los turnos convertidos no se distinguen de los flexibles sin ventana ya existentes.
        }
    }
}
