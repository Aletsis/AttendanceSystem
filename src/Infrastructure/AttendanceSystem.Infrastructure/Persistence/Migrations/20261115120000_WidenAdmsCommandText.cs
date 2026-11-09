using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// El texto de los comandos ADMS pasa a <c>text</c>: un rostro o una foto en base64 no caben en 2000 caracteres
    /// y enviar ese empleado a un reloj ADMS fallaba al encolar el comando.
    /// </summary>
    public partial class WidenAdmsCommandText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CommandText",
                table: "AdmsCommands",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Los comandos que no quepan se recortan para poder volver al tipo anterior.
            migrationBuilder.Sql(@"UPDATE ""AdmsCommands"" SET ""CommandText"" = LEFT(""CommandText"", 2000) WHERE LENGTH(""CommandText"") > 2000;");

            migrationBuilder.AlterColumn<string>(
                name: "CommandText",
                table: "AdmsCommands",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
