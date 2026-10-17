using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceIntegrityAndOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Agregar columnas aditivas a AttendanceRecords
            migrationBuilder.AddColumn<DateTime>(
                name: "OriginalCheckTime",
                table: "AttendanceRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WorkDate",
                table: "AttendanceRecords",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "AttendanceRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "AttendanceRecords",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalCheckTypeId",
                table: "AttendanceRecords",
                type: "integer",
                nullable: true);

            // 2. Crear tabla OutboxMessages
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedOnUtc_OccurredOnUtc",
                table: "OutboxMessages",
                columns: new[] { "ProcessedOnUtc", "OccurredOnUtc" });

            // 3. F03-P03: Reconciliación segura de duplicados existentes antes de crear el índice único
            // Preservar cualquier registro que esté referenciado por DailyAttendances.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'AttendanceRecords') THEN
                        -- Deduplicar preservando los registros enlazados a DailyAttendances
                        DELETE FROM ""AttendanceRecords"" a
                        WHERE a.""Id"" NOT IN (
                            SELECT DISTINCT coalesce(""CheckInRecordId"", '00000000-0000-0000-0000-000000000000'::uuid) FROM ""DailyAttendances"" WHERE ""CheckInRecordId"" IS NOT NULL
                            UNION
                            SELECT DISTINCT coalesce(""Block1CheckOutRecordId"", '00000000-0000-0000-0000-000000000000'::uuid) FROM ""DailyAttendances"" WHERE ""Block1CheckOutRecordId"" IS NOT NULL
                            UNION
                            SELECT DISTINCT coalesce(""Block2CheckInRecordId"", '00000000-0000-0000-0000-000000000000'::uuid) FROM ""DailyAttendances"" WHERE ""Block2CheckInRecordId"" IS NOT NULL
                            UNION
                            SELECT DISTINCT coalesce(""CheckOutRecordId"", '00000000-0000-0000-0000-000000000000'::uuid) FROM ""DailyAttendances"" WHERE ""CheckOutRecordId"" IS NOT NULL
                        )
                        AND a.""Id"" IN (
                            SELECT ""Id"" FROM (
                                SELECT ""Id"",
                                       ROW_NUMBER() OVER(PARTITION BY ""EmployeeId"", ""CheckTime"" ORDER BY ""StatusId"" DESC, ""Id"") as rnum
                                FROM ""AttendanceRecords""
                            ) t
                            WHERE t.rnum > 1
                        );
                    END IF;
                END $$;
            ");

            // 4. Reemplazar índice no-único por índice único en (EmployeeId, CheckTime) - H09
            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_EmployeeId_CheckTime",
                table: "AttendanceRecords");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_EmployeeId_CheckTime",
                table: "AttendanceRecords",
                columns: new[] { "EmployeeId", "CheckTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_EmployeeId_WorkDate",
                table: "AttendanceRecords",
                columns: new[] { "EmployeeId", "WorkDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_EmployeeId_WorkDate",
                table: "AttendanceRecords");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_EmployeeId_CheckTime",
                table: "AttendanceRecords");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_EmployeeId_CheckTime",
                table: "AttendanceRecords",
                columns: new[] { "EmployeeId", "CheckTime" });

            migrationBuilder.DropColumn(
                name: "OriginalCheckTime",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "WorkDate",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "OriginalCheckTypeId",
                table: "AttendanceRecords");
        }
    }
}
