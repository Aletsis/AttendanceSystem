using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Opción por reloj para recibir sus marcajes en tiempo real por SDK, además de las descargas. Apagada en los relojes
    /// existentes.
    /// </summary>
    public partial class AddDeviceRealtimeEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "RealtimeEnabled", table: "Devices", type: "boolean", nullable: false, defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RealtimeEnabled", table: "Devices");
        }
    }
}
