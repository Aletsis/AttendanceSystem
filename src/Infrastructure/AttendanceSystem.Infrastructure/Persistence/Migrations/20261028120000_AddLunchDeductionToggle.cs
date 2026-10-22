using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Interruptor global del descuento de la comida completa; desactivado por defecto.
    /// </summary>
    public partial class AddLunchDeductionToggle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "DeductLunch", table: "SystemConfiguration", type: "boolean", nullable: false, defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DeductLunch", table: "SystemConfiguration");
        }
    }
}
