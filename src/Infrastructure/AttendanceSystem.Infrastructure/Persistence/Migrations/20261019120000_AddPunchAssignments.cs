using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// F02-01 / F02-02: relación explícita marcaje–jornada–posición.
    /// No rellena datos: las asignaciones heredadas se diagnostican y reparan con
    /// IPunchAssignmentDiagnosticService (F02-07), porque pueden contener referencias múltiples.
    /// La unicidad de posición por jornada se valida en PunchAssignmentPolicy: un índice único no
    /// diferible fallaría al intercambiar posiciones con actualizaciones en sitio.
    /// </summary>
    public partial class AddPunchAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clave alterna requerida por la FK compuesta de pertenencia (marcaje, empleado).
            migrationBuilder.AddUniqueConstraint(
                name: "AK_AttendanceRecords_Id_EmployeeId",
                table: "AttendanceRecords",
                columns: new[] { "Id", "EmployeeId" });

            migrationBuilder.CreateTable(
                name: "PunchAssignments",
                columns: table => new
                {
                    AttendanceRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Origin = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PunchAssignments", x => x.AttendanceRecordId);
                    table.CheckConstraint("CK_PunchAssignments_Role", "\"Role\" BETWEEN 1 AND 8");
                    table.CheckConstraint("CK_PunchAssignments_Origin", "\"Origin\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_PunchAssignments_AttendanceRecords_Owner",
                        columns: x => new { x.AttendanceRecordId, x.EmployeeId },
                        principalTable: "AttendanceRecords",
                        principalColumns: new[] { "Id", "EmployeeId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PunchAssignments_EmployeeId_WorkDate",
                table: "PunchAssignments",
                columns: new[] { "EmployeeId", "WorkDate" });

            // Necesario para la FK compuesta (búsqueda inversa al borrar marcajes).
            migrationBuilder.CreateIndex(
                name: "IX_PunchAssignments_AttendanceRecordId_EmployeeId",
                table: "PunchAssignments",
                columns: new[] { "AttendanceRecordId", "EmployeeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PunchAssignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AttendanceRecords_Id_EmployeeId",
                table: "AttendanceRecords");
        }
    }
}
