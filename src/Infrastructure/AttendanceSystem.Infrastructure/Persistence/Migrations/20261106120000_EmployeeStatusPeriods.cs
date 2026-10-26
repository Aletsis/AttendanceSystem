using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Caso 16: vacaciones, incapacidades y bajas temporales con fechas. Los empleados que ya tenían uno de esos estados
    /// (sin fechas) quedan con un período abierto desde hoy, hasta que RH los regrese a Alta.
    /// </summary>
    public partial class EmployeeStatusPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeStatusPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeStatusPeriods", x => x.Id);
                    table.ForeignKey("FK_EmployeeStatusPeriods_Employees_EmployeeId", x => x.EmployeeId, "Employees", "Id", onDelete: ReferentialAction.Cascade);
                    table.CheckConstraint("CK_EmployeeStatusPeriods_Range", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                });
            migrationBuilder.CreateIndex(name: "IX_EmployeeStatusPeriods_EmployeeId", table: "EmployeeStatusPeriods", column: "EmployeeId");

            // Estados 2 = Baja temporal, 3 = Vacaciones, 4 = Incapacidad.
            migrationBuilder.Sql(@"
INSERT INTO ""EmployeeStatusPeriods"" (""Id"", ""EmployeeId"", ""Status"", ""StartDate"", ""EndDate"")
SELECT gen_random_uuid(), e.""Id"", e.""Status"", GREATEST(CURRENT_DATE, e.""HireDate""::date), NULL
  FROM ""Employees"" e
 WHERE e.""Status"" IN (2, 3, 4);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmployeeStatusPeriods");
        }
    }
}
