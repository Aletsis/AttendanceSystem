using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Faltante de la bolsa semanal: minutos que faltaron para la meta, guardados en el último día con bolsa de la
    /// semana. Se llena al recalcular.
    /// </summary>
    public partial class AddWeeklyShortfall : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "WeeklyShortfallMinutes", table: "DailyAttendances", type: "integer", nullable: false, defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "WeeklyShortfallMinutes", table: "DailyAttendances");
        }
    }
}
