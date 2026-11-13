using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Separa el cursor temporal de ADMS (Stamp) de <c>Devices.LastDownloadAt</c>, garantizando que se conserve
    /// siempre en la hora civil del reloj y no se contamine con instantes UTC de descargas o fallas.
    /// </summary>
    public partial class AddDeviceAdmsStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AdmsStamp",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            // Inicializar el nuevo cursor con LastDownloadAt para dispositivos ADMS existentes que ya tenían un cursor
            migrationBuilder.Sql(@"UPDATE ""Devices"" SET ""AdmsStamp"" = ""LastDownloadAt"" WHERE ""DownloadMethod"" = 1 AND ""LastDownloadAt"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdmsStamp",
                table: "Devices");
        }
    }
}
