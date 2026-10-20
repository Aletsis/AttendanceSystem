using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Outbox con reclamación (lease), backoff y mensajes muertos; solicitudes de consolidación con versión,
    /// lease, backoff y una sola solicitud activa por empleado.
    /// Datos heredados: las solicitudes activas duplicadas se fusionan en la más antigua (las demás quedan
    /// Superseded); las que estaban en Processing vuelven a Pending (no tenían consumidor); los mensajes que
    /// agotaron sus intentos sin procesarse pasan a mensajes muertos visibles.
    /// </summary>
    public partial class AddDurableConsolidationAndOutboxLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "LockedBy", table: "OutboxMessages", type: "character varying(200)", maxLength: 200, nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "LockedUntilUtc", table: "OutboxMessages", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "NextAttemptOnUtc", table: "OutboxMessages", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "DeadLetteredOnUtc", table: "OutboxMessages", type: "timestamp with time zone", nullable: true);

            migrationBuilder.Sql(@"
UPDATE ""OutboxMessages""
   SET ""DeadLetteredOnUtc"" = now()
 WHERE ""ProcessedOnUtc"" IS NULL AND ""RetryCount"" >= 5;");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedOnUtc_DeadLetteredOnUtc_NextAttemptOnUtc",
                table: "OutboxMessages",
                columns: new[] { "ProcessedOnUtc", "DeadLetteredOnUtc", "NextAttemptOnUtc" });

            migrationBuilder.AddColumn<DateTime>(name: "LastRequestedAt", table: "AttendanceConsolidationRequests", type: "timestamp with time zone", nullable: false, defaultValueSql: "now()");
            migrationBuilder.AddColumn<int>(name: "Version", table: "AttendanceConsolidationRequests", type: "integer", nullable: false, defaultValue: 1);
            migrationBuilder.AddColumn<string>(name: "ClaimedBy", table: "AttendanceConsolidationRequests", type: "character varying(200)", maxLength: 200, nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "LeaseExpiresAt", table: "AttendanceConsolidationRequests", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ClaimedVersion", table: "AttendanceConsolidationRequests", type: "integer", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "NextAttemptAt", table: "AttendanceConsolidationRequests", type: "timestamp with time zone", nullable: true);

            migrationBuilder.Sql(@"
UPDATE ""AttendanceConsolidationRequests"" SET ""LastRequestedAt"" = ""RequestedAt"";

WITH ranked AS (
    SELECT ""Id"",
           row_number() OVER (PARTITION BY ""EmployeeId"" ORDER BY ""RequestedAt"", ""Id"") AS rn,
           min(""StartDate"") OVER (PARTITION BY ""EmployeeId"") AS s,
           max(""EndDate"") OVER (PARTITION BY ""EmployeeId"") AS e
      FROM ""AttendanceConsolidationRequests""
     WHERE ""Status"" IN (0, 1))
UPDATE ""AttendanceConsolidationRequests"" r
   SET ""StartDate"" = CASE WHEN ranked.rn = 1 THEN ranked.s ELSE r.""StartDate"" END,
       ""EndDate"" = CASE WHEN ranked.rn = 1 THEN ranked.e ELSE r.""EndDate"" END,
       ""Status"" = CASE WHEN ranked.rn = 1 THEN 0 ELSE 4 END,
       ""ProcessedAt"" = CASE WHEN ranked.rn = 1 THEN r.""ProcessedAt"" ELSE now() END,
       ""ErrorMessage"" = CASE WHEN ranked.rn = 1 THEN r.""ErrorMessage"" ELSE 'Fusionada con la solicitud activa del empleado' END
  FROM ranked
 WHERE r.""Id"" = ranked.""Id"";");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceConsolidationRequests_EmployeeId_Active",
                table: "AttendanceConsolidationRequests",
                column: "EmployeeId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_AttendanceConsolidationRequests_EmployeeId_Active", table: "AttendanceConsolidationRequests");
            migrationBuilder.DropColumn(name: "LastRequestedAt", table: "AttendanceConsolidationRequests");
            migrationBuilder.DropColumn(name: "Version", table: "AttendanceConsolidationRequests");
            migrationBuilder.DropColumn(name: "ClaimedBy", table: "AttendanceConsolidationRequests");
            migrationBuilder.DropColumn(name: "LeaseExpiresAt", table: "AttendanceConsolidationRequests");
            migrationBuilder.DropColumn(name: "ClaimedVersion", table: "AttendanceConsolidationRequests");
            migrationBuilder.DropColumn(name: "NextAttemptAt", table: "AttendanceConsolidationRequests");

            migrationBuilder.DropIndex(name: "IX_OutboxMessages_ProcessedOnUtc_DeadLetteredOnUtc_NextAttemptOnUtc", table: "OutboxMessages");
            migrationBuilder.DropColumn(name: "LockedBy", table: "OutboxMessages");
            migrationBuilder.DropColumn(name: "LockedUntilUtc", table: "OutboxMessages");
            migrationBuilder.DropColumn(name: "NextAttemptOnUtc", table: "OutboxMessages");
            migrationBuilder.DropColumn(name: "DeadLetteredOnUtc", table: "OutboxMessages");
        }
    }
}
