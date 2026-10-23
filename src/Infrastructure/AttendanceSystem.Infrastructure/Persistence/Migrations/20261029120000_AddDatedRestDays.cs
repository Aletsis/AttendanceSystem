using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Días de descanso con vigencia: cada asignación de horario guarda los descansos que rigen desde su fecha. Las
    /// vigencias existentes toman los descansos actuales del empleado (lista, o el día principal heredado).
    /// </summary>
    public partial class AddDatedRestDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "RestDays", table: "EmployeeScheduleAssignments", type: "character varying(50)", maxLength: 50, nullable: true);

            migrationBuilder.Sql("""
                UPDATE "EmployeeScheduleAssignments" a
                SET "RestDays" = COALESCE(NULLIF(e."RestDays", ''), e."RestDay"::text, '')
                FROM "Employees" e
                WHERE a."EmployeeId" = e."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RestDays", table: "EmployeeScheduleAssignments");
        }
    }
}
