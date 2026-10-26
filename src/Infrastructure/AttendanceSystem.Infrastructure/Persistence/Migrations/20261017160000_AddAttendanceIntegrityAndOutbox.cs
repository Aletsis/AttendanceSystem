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

            // 3. F03-P03: Reconciliación de duplicados existentes antes de crear el índice único.
            // Un mismo marcaje (empleado, instante) descargado más de una vez. De cada grupo se conserva uno: primero el
            // que ya usa una jornada, luego el de mayor estado. Las jornadas que apuntaban a una copia pasan a apuntar al
            // conservado y las copias se eliminan. (Antes se conservaban todas las copias enlazadas, y si dos copias
            // estaban enlazadas, o la enlazada no era la primera del grupo, el índice único no se podía crear.)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'AttendanceRecords') THEN
                        CREATE TEMP TABLE punch_duplicates ON COMMIT DROP AS
                        SELECT t.""Id"", t.""KeeperId""
                        FROM (
                            SELECT a.""Id"",
                                   FIRST_VALUE(a.""Id"") OVER w AS ""KeeperId""
                            FROM ""AttendanceRecords"" a
                            WINDOW w AS (
                                PARTITION BY a.""EmployeeId"", a.""CheckTime""
                                ORDER BY (EXISTS (
                                              SELECT 1 FROM ""DailyAttendances"" d
                                              WHERE a.""Id"" IN (d.""CheckInRecordId"", d.""Block1CheckOutRecordId"", d.""Block2CheckInRecordId"", d.""CheckOutRecordId""))) DESC,
                                         a.""StatusId"" DESC, a.""Id"")
                        ) t
                        WHERE t.""Id"" <> t.""KeeperId"";

                        UPDATE ""DailyAttendances"" d SET ""CheckInRecordId"" = p.""KeeperId"" FROM punch_duplicates p WHERE d.""CheckInRecordId"" = p.""Id"";
                        UPDATE ""DailyAttendances"" d SET ""Block1CheckOutRecordId"" = p.""KeeperId"" FROM punch_duplicates p WHERE d.""Block1CheckOutRecordId"" = p.""Id"";
                        UPDATE ""DailyAttendances"" d SET ""Block2CheckInRecordId"" = p.""KeeperId"" FROM punch_duplicates p WHERE d.""Block2CheckInRecordId"" = p.""Id"";
                        UPDATE ""DailyAttendances"" d SET ""CheckOutRecordId"" = p.""KeeperId"" FROM punch_duplicates p WHERE d.""CheckOutRecordId"" = p.""Id"";

                        DELETE FROM ""AttendanceRecords"" a USING punch_duplicates p WHERE a.""Id"" = p.""Id"";
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
