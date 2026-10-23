using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Caso 12: RH autoriza las extras de un día de un empleado sin extras autorizadas (completas o en bloques de 30 min),
    /// ligadas al excedente que vio; los empleados sin extras autorizadas no conservan redondeo, topes ni extras antes de
    /// la entrada.
    /// </summary>
    public partial class AuthorizePartialOvertime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthorizedOvertimeMinutes",
                table: "AttendanceAdjustments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthorizedOvertimeForRawMinutes",
                table: "AttendanceAdjustments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthorizedOvertimeMinutes",
                table: "DailyAttendances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthorizedOvertimeForRawMinutes",
                table: "DailyAttendances",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""Employees""
                SET ""OvertimeCalculationMethod"" = 0, ""OvertimeCapType"" = 0, ""OvertimeCapMinutes"" = NULL,
                    ""CalculateOvertimeBeforeEntry"" = FALSE
                WHERE NOT ""OvertimeAuthorized"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AuthorizedOvertimeMinutes", table: "AttendanceAdjustments");
            migrationBuilder.DropColumn(name: "AuthorizedOvertimeForRawMinutes", table: "AttendanceAdjustments");
            migrationBuilder.DropColumn(name: "AuthorizedOvertimeMinutes", table: "DailyAttendances");
            migrationBuilder.DropColumn(name: "AuthorizedOvertimeForRawMinutes", table: "DailyAttendances");
        }
    }
}
