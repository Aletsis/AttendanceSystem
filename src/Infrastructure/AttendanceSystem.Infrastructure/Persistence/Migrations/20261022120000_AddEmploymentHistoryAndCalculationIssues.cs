using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Vigencias laborales (períodos de empleo y asignaciones de horario, tipo y sucursal) e incidencias de cálculo
    /// visibles en la jornada. Datos heredados: un período por empleado [HireDate, TerminationDate] y una asignación
    /// con la configuración actual desde el ingreso. La historia anterior no es reconstruible y no se inventa.
    /// </summary>
    public partial class AddEmploymentHistoryAndCalculationIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "CalculationIssue", table: "DailyAttendances", type: "character varying(500)", maxLength: 500, nullable: true);
            migrationBuilder.AddColumn<bool>(name: "HasOpenInterval", table: "DailyAttendances", type: "boolean", nullable: false, defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EmployeeEmploymentPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeEmploymentPeriods", x => x.Id);
                    table.ForeignKey("FK_EmployeeEmploymentPeriods_Employees_EmployeeId", x => x.EmployeeId, "Employees", "Id", onDelete: ReferentialAction.Cascade);
                    table.CheckConstraint("CK_EmployeeEmploymentPeriods_Range", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                });
            migrationBuilder.CreateIndex(name: "IX_EmployeeEmploymentPeriods_EmployeeId", table: "EmployeeEmploymentPeriods", column: "EmployeeId");

            migrationBuilder.CreateTable(
                name: "EmployeeScheduleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShiftType = table.Column<int>(type: "integer", nullable: true),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeScheduleAssignments", x => x.Id);
                    table.ForeignKey("FK_EmployeeScheduleAssignments_Employees_EmployeeId", x => x.EmployeeId, "Employees", "Id", onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(name: "IX_EmployeeScheduleAssignments_EmployeeId", table: "EmployeeScheduleAssignments", column: "EmployeeId");

            // HireDate y TerminationDate guardan la fecha a medianoche etiquetada como UTC: ::date a secas depende de la
            // zona de la sesión y, al oeste de UTC, da el día anterior (ver 20261110120000_ConvertLegacyCivilTimes).
            migrationBuilder.Sql(@"
INSERT INTO ""EmployeeEmploymentPeriods"" (""Id"", ""EmployeeId"", ""StartDate"", ""EndDate"")
SELECT gen_random_uuid(), e.""Id"", (e.""HireDate"" AT TIME ZONE 'UTC')::date,
       CASE WHEN e.""TerminationDate"" IS NOT NULL AND e.""TerminationDate"" >= e.""HireDate""
            THEN (e.""TerminationDate"" AT TIME ZONE 'UTC')::date END
  FROM ""Employees"" e;

INSERT INTO ""EmployeeScheduleAssignments"" (""Id"", ""EmployeeId"", ""EffectiveFrom"", ""ScheduleId"", ""ShiftType"", ""BranchId"")
SELECT gen_random_uuid(), e.""Id"", (e.""HireDate"" AT TIME ZONE 'UTC')::date, e.""ScheduleId"", e.""ShiftType"", e.""BranchId""
  FROM ""Employees"" e;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmployeeScheduleAssignments");
            migrationBuilder.DropTable(name: "EmployeeEmploymentPeriods");
            migrationBuilder.DropColumn(name: "CalculationIssue", table: "DailyAttendances");
            migrationBuilder.DropColumn(name: "HasOpenInterval", table: "DailyAttendances");
        }
    }
}
