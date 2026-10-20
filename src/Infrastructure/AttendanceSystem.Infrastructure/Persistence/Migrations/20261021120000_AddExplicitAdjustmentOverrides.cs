using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Override explícito por posición (sin decisión / asignación forzada / vacío forzado), revisión del ajuste,
    /// historial auditado de decisiones y rol de asignación para marcajes excluidos manualmente.
    /// Datos heredados: sólo una posición con marcaje y hora explícitos se marca como asignación forzada.
    /// Las desasignaciones antiguas no dejaron rastro y no se infieren (las reporta el diagnóstico de ajustes).
    /// </summary>
    public partial class AddExplicitAdjustmentOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var column in new[] { "CheckInOverride", "Block1CheckOutOverride", "Block2CheckInOverride", "CheckOutOverride" })
            {
                migrationBuilder.AddColumn<int>(name: column, table: "AttendanceAdjustments", type: "integer", nullable: false, defaultValue: 0);
            }
            migrationBuilder.AddColumn<int>(name: "Revision", table: "AttendanceAdjustments", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.Sql(@"
UPDATE ""AttendanceAdjustments"" SET ""CheckInOverride"" = 1 WHERE ""CheckInRecordId"" IS NOT NULL AND ""CheckInTimeOverride"" IS NOT NULL;
UPDATE ""AttendanceAdjustments"" SET ""Block1CheckOutOverride"" = 1 WHERE ""Block1CheckOutRecordId"" IS NOT NULL AND ""Block1CheckOutTimeOverride"" IS NOT NULL;
UPDATE ""AttendanceAdjustments"" SET ""Block2CheckInOverride"" = 1 WHERE ""Block2CheckInRecordId"" IS NOT NULL AND ""Block2CheckInTimeOverride"" IS NOT NULL;
UPDATE ""AttendanceAdjustments"" SET ""CheckOutOverride"" = 1 WHERE ""CheckOutRecordId"" IS NOT NULL AND ""CheckOutTimeOverride"" IS NOT NULL;
UPDATE ""AttendanceAdjustments"" SET ""Revision"" = 1;");

            migrationBuilder.CreateTable(
                name: "AttendanceAdjustmentHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdjustmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Before = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    After = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ChangedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_AttendanceAdjustmentHistory", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_AttendanceAdjustmentHistory_EmployeeId_Date_Revision", table: "AttendanceAdjustmentHistory", columns: new[] { "EmployeeId", "Date", "Revision" });
            migrationBuilder.CreateIndex(name: "IX_AttendanceAdjustmentHistory_AdjustmentId", table: "AttendanceAdjustmentHistory", column: "AdjustmentId");

            migrationBuilder.DropCheckConstraint(name: "CK_PunchAssignments_Role", table: "PunchAssignments");
            migrationBuilder.AddCheckConstraint(name: "CK_PunchAssignments_Role", table: "PunchAssignments", sql: "\"Role\" BETWEEN 1 AND 9");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""PunchAssignments"" WHERE ""Role"" = 9;");
            migrationBuilder.DropCheckConstraint(name: "CK_PunchAssignments_Role", table: "PunchAssignments");
            migrationBuilder.AddCheckConstraint(name: "CK_PunchAssignments_Role", table: "PunchAssignments", sql: "\"Role\" BETWEEN 1 AND 8");

            migrationBuilder.DropTable(name: "AttendanceAdjustmentHistory");
            foreach (var column in new[] { "CheckInOverride", "Block1CheckOutOverride", "Block2CheckInOverride", "CheckOutOverride", "Revision" })
            {
                migrationBuilder.DropColumn(name: column, table: "AttendanceAdjustments");
            }
        }
    }
}
