using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Caso 17: cierres de periodo de asistencia (nunca se borran; reabrir deja autor y motivo) y la hora en que el
    /// sistema recibió cada marcaje, para detectar los que llegan a un periodo cerrado.
    /// </summary>
    public partial class AttendancePeriodClosures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendancePeriodClosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Automatic = table.Column<bool>(type: "boolean", nullable: false),
                    ReopenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReopenedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReopenReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendancePeriodClosures", x => x.Id);
                    table.CheckConstraint("CK_AttendancePeriodClosures_Range", "\"EndDate\" >= \"StartDate\"");
                });
            migrationBuilder.CreateIndex(name: "IX_AttendancePeriodClosures_StartDate_EndDate", table: "AttendancePeriodClosures", columns: new[] { "StartDate", "EndDate" });

            migrationBuilder.AddColumn<DateTime>(name: "ReceivedAt", table: "AttendanceRecords", type: "timestamp with time zone", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReceivedAt", table: "AttendanceRecords");
            migrationBuilder.DropTable(name: "AttendancePeriodClosures");
        }
    }
}
