using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// La cola de comandos ADMS pasa a ser la tabla AdmsCommands (antes una copia de la cola en memoria): el proceso que
    /// encola (WPF o la web) y el que atiende al reloj pueden ser distintos. La respuesta que espera quien encoló (INFO)
    /// viaja en la fila del comando.
    /// </summary>
    public partial class AddAdmsCommandResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ResultPayload", table: "AdmsCommands", type: "text", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ResultPayload", table: "AdmsCommands");
        }
    }
}
