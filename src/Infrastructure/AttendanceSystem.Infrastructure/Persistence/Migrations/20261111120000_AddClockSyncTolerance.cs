using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Caso 18: la tolerancia de desfase antes de resincronizar un reloj deja de ser fija (2 minutos) y pasa a la
    /// política configurable de la hora de los relojes.
    /// </summary>
    public partial class AddClockSyncTolerance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "QuarantineClockSyncToleranceMinutes", table: "SystemConfiguration", type: "integer", nullable: false, defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "QuarantineClockSyncToleranceMinutes", table: "SystemConfiguration");
        }
    }
}
