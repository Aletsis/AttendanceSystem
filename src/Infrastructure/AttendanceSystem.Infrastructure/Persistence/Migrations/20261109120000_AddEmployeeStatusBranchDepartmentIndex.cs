using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Crea el índice de empleados por estado, sucursal y departamento que el modelo declaraba sin migración, y
    /// resincroniza el snapshot: las migraciones escritas a mano no lo actualizaban, así que la siguiente migración
    /// generada habría vuelto a crear tablas y columnas que ya existen. El Designer de ésta guarda el modelo completo.
    /// </summary>
    public partial class AddEmployeeStatusBranchDepartmentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Employees_Status_BranchId_DepartmentId",
                table: "Employees",
                columns: new[] { "Status", "BranchId", "DepartmentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Employees_Status_BranchId_DepartmentId",
                table: "Employees");
        }
    }
}
