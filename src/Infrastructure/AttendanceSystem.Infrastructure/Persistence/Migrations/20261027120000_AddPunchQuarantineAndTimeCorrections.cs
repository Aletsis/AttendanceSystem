using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Cuarentena de marcajes con hora imposible (reloj sin fecha o adelantado) y auditoría de las correcciones de hora
    /// de un reloj (zona y/o desfase), con los valores anteriores de cada marcaje corregido.
    /// </summary>
    public partial class AddPunchQuarantineAndTimeCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuarantinedPunches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReportedTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VerifyMethodCode = table.Column<int>(type: "integer", nullable: false),
                    CheckTypeCode = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReleasedTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasedRecordId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_QuarantinedPunches", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_QuarantinedPunches_Status_ReceivedAtUtc", table: "QuarantinedPunches", columns: new[] { "Status", "ReceivedAtUtc" });
            migrationBuilder.CreateIndex(name: "IX_QuarantinedPunches_DeviceId_EmployeeId_ReportedTime", table: "QuarantinedPunches",
                columns: new[] { "DeviceId", "EmployeeId", "ReportedTime" }, unique: true);

            migrationBuilder.CreateTable(
                name: "PunchTimeCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AppliedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_PunchTimeCorrections", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_PunchTimeCorrections_AppliedAtUtc", table: "PunchTimeCorrections", column: "AppliedAtUtc");

            migrationBuilder.CreateTable(
                name: "PunchTimeCorrectionEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendanceRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OldCheckTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OldCivilTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OldWorkDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OldTimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NewCheckTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NewCivilTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NewWorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NewTimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PunchTimeCorrectionEntries", x => x.Id);
                    table.ForeignKey("FK_PunchTimeCorrectionEntries_PunchTimeCorrections_CorrectionId", x => x.CorrectionId,
                        "PunchTimeCorrections", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_PunchTimeCorrectionEntries_CorrectionId", table: "PunchTimeCorrectionEntries", column: "CorrectionId");
            migrationBuilder.CreateIndex(name: "IX_PunchTimeCorrectionEntries_AttendanceRecordId", table: "PunchTimeCorrectionEntries", column: "AttendanceRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PunchTimeCorrectionEntries");
            migrationBuilder.DropTable(name: "PunchTimeCorrections");
            migrationBuilder.DropTable(name: "QuarantinedPunches");
        }
    }
}
