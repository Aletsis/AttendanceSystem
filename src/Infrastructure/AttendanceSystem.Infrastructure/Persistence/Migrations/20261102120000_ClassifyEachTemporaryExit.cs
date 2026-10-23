using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Caso 11: cada salida temporal se clasifica por separado y la comida sólo descuenta su excedente.
    /// <list type="bullet">
    /// <item>"Descontar comida" se reemplaza por "Ventana de comida" (activa por defecto). Quien descontaba la comida
    /// completa pasa a 0 minutos con goce, que da el mismo descuento.</item>
    /// <item>Las jornadas guardan el detalle por salida (se llena al recalcular) y los minutos de salidas descontados,
    /// que para las jornadas existentes se derivan de su clasificación del día.</item>
    /// <item>Los ajustes guardan la clasificación de cada salida.</item>
    /// </list>
    /// </summary>
    public partial class ClassifyEachTemporaryExit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "LunchWindowEnabled", table: "SystemConfiguration", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.Sql("UPDATE \"SystemConfiguration\" SET \"PaidLunchMinutes\" = 0 WHERE \"DeductLunch\";");
            migrationBuilder.DropColumn(name: "DeductLunch", table: "SystemConfiguration");

            migrationBuilder.AddColumn<string>(name: "TemporaryExitsData", table: "DailyAttendances", type: "text", nullable: true);
            migrationBuilder.AddColumn<int>(name: "PaidLunchMinutes", table: "DailyAttendances", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "DeductedExitMinutes", table: "DailyAttendances", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.Sql(
                "UPDATE \"DailyAttendances\" SET \"DeductedExitMinutes\" = \"TemporaryExitMinutes\" " +
                "WHERE \"HasTemporaryExits\" AND \"TemporaryExitStatus\" IN (0, 2);");
            migrationBuilder.DropColumn(name: "LunchExcessForgiven", table: "DailyAttendances");

            migrationBuilder.AddColumn<string>(name: "ExitClassifications", table: "AttendanceAdjustments", type: "text", nullable: false, defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ExitClassifications", table: "AttendanceAdjustments");

            migrationBuilder.AddColumn<bool>(name: "LunchExcessForgiven", table: "DailyAttendances", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.DropColumn(name: "DeductedExitMinutes", table: "DailyAttendances");
            migrationBuilder.DropColumn(name: "PaidLunchMinutes", table: "DailyAttendances");
            migrationBuilder.DropColumn(name: "TemporaryExitsData", table: "DailyAttendances");

            migrationBuilder.AddColumn<bool>(name: "DeductLunch", table: "SystemConfiguration", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.DropColumn(name: "LunchWindowEnabled", table: "SystemConfiguration");
        }
    }
}
