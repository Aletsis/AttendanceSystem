using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftTypeToShiftDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShiftType",
                table: "ShiftDays",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Migrar turnos de "Jornada 24 Horas" (5) a "Continuo" (4) con jornada de 24 horas
            migrationBuilder.Sql("UPDATE \"Shifts\" SET \"ShiftType\" = 4, \"WorkHours\" = '24:00:00'::interval WHERE \"ShiftType\" = 5;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShiftType",
                table: "ShiftDays");
        }
    }
}
