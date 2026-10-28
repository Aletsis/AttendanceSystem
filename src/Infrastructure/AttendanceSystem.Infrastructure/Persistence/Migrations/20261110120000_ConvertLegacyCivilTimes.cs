using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Repara los datos que dejó la versión anterior a la normalización de zonas horarias (20261017160000 en adelante).
    /// <para>
    /// Aquella versión guardaba en <c>AttendanceRecords.CheckTime</c> la hora del reloj etiquetada como UTC, sin
    /// convertirla: un marcaje de las 09:01 en Ciudad de México quedaba como 09:01Z. Esta versión toma
    /// <c>CheckTime</c> como el instante real y la hora del reloj de <c>OriginalCheckTime</c>, así que esas checadas
    /// se mostraban seis horas antes. Se reconocen por <c>OriginalCheckTime</c> nulo: toda checada creada desde
    /// entonces lo trae. No se corrige el instante de una checada, sino cómo estaba guardado: 09:01Z nunca fue el
    /// instante real. Las heredadas que, ya convertidas, coinciden con una checada nueva (se volvió a descargar el
    /// mismo rango del reloj) se anulan, no se borran.
    /// </para>
    /// <para>
    /// También corrige las fechas que 20261022120000 y 20261106120000 calcularon con <c>timestamptz::date</c>, que
    /// depende de la zona de la sesión: en un servidor al oeste de UTC (America/Chicago, por ejemplo), cada alta,
    /// baja y vigencia inicial quedó un día antes. Esas migraciones ya usan <c>AT TIME ZONE 'UTC'</c>; aquí sólo se
    /// corrigen las filas que quedaron exactamente un día antes, así que en las demás bases no cambia nada.
    /// </para>
    /// <para>
    /// Las jornadas ya calculadas no se tocan: sin <c>TimeZoneId</c> se muestran tal como se guardaron, y al
    /// recalcularse usarán las checadas ya convertidas. Sin <c>Down</c>: el respaldo previo a la migración es la
    /// única vuelta atrás.
    /// </para>
    /// </summary>
    public partial class ConvertLegacyCivilTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Todo lo que sigue se calcula en UTC, sea cual sea la zona del servidor.
            migrationBuilder.Sql("SET LOCAL TimeZone = 'UTC';");

            // Zona de cada checada heredada: la del dispositivo o, si no tiene una que PostgreSQL conozca, la del sistema.
            migrationBuilder.Sql(@"
CREATE TEMP TABLE legacy_punches ON COMMIT DROP AS
WITH system_zone AS (
    SELECT coalesce(
               (SELECT z.name FROM pg_timezone_names z
                 WHERE z.name = (SELECT ""DefaultTimeZoneId"" FROM ""SystemConfiguration"" ORDER BY ""Id"" LIMIT 1)),
               'America/Mexico_City') AS tz),
device_zones AS (
    SELECT d.""Id"", z.name AS tz
      FROM ""Devices"" d
      JOIN pg_timezone_names z ON z.name = d.""TimeZoneId"")
SELECT a.""Id"", a.""EmployeeId"", a.""VoidedAt"", coalesce(dz.tz, s.tz) AS tz,
       (a.""CheckTime"" AT TIME ZONE 'UTC') AT TIME ZONE coalesce(dz.tz, s.tz) AS instant
  FROM ""AttendanceRecords"" a
 CROSS JOIN system_zone s
  LEFT JOIN device_zones dz ON dz.""Id"" = a.""DeviceId""
 WHERE a.""OriginalCheckTime"" IS NULL;");

            // Una heredada que, convertida, coincide con una checada vigente nueva, o con otra heredada (horas que no
            // existen por el cambio de horario), se anula. Se conserva la nueva o, entre heredadas, la de menor Id.
            migrationBuilder.Sql(@"
WITH candidates AS (
    SELECT l.""Id"", l.""EmployeeId"", l.instant, true AS legacy
      FROM legacy_punches l
     WHERE l.""VoidedAt"" IS NULL
    UNION ALL
    SELECT a.""Id"", a.""EmployeeId"", a.""CheckTime"", false
      FROM ""AttendanceRecords"" a
      JOIN (SELECT DISTINCT ""EmployeeId"", instant FROM legacy_punches WHERE ""VoidedAt"" IS NULL) l
        ON l.""EmployeeId"" = a.""EmployeeId"" AND l.instant = a.""CheckTime""
     WHERE a.""OriginalCheckTime"" IS NOT NULL AND a.""VoidedAt"" IS NULL),
ranked AS (
    SELECT ""Id"", legacy,
           row_number() OVER (PARTITION BY ""EmployeeId"", instant ORDER BY legacy, ""Id"") AS rn
      FROM candidates)
UPDATE ""AttendanceRecords"" a
   SET ""VoidedAt"" = now(),
       ""VoidedBy"" = 'Sistema (actualización)',
       ""VoidReason"" = 'Copia de una checada registrada otra vez después de la actualización'
  FROM ranked r
 WHERE r.""Id"" = a.""Id"" AND r.legacy AND r.rn > 1;");

            // Al convertir, una checada puede tomar momentáneamente el instante que otra todavía no ha dejado
            // (09:00 → 15:00 mientras la de las 15:00 sigue sin convertir): el índice único se rehace al final.
            migrationBuilder.DropIndex(name: "IX_AttendanceRecords_EmployeeId_CheckTime", table: "AttendanceRecords");

            migrationBuilder.Sql(@"
UPDATE ""AttendanceRecords"" a
   SET ""OriginalCheckTime"" = a.""CheckTime"",
       ""CheckTime"" = l.instant,
       ""TimeZoneId"" = coalesce(a.""TimeZoneId"", l.tz),
       ""WorkDate"" = coalesce(a.""WorkDate"", (a.""CheckTime"" AT TIME ZONE 'UTC')::date)
  FROM legacy_punches l
 WHERE l.""Id"" = a.""Id"";");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_EmployeeId_CheckTime",
                table: "AttendanceRecords",
                columns: new[] { "EmployeeId", "CheckTime" },
                unique: true,
                filter: "\"VoidedAt\" IS NULL");

            // Fechas de alta, baja y vigencias iniciales que quedaron un día antes.
            migrationBuilder.Sql(@"
UPDATE ""EmployeeEmploymentPeriods"" p
   SET ""StartDate"" = p.""StartDate"" + 1
  FROM ""Employees"" e
 WHERE e.""Id"" = p.""EmployeeId""
   AND p.""StartDate"" = (e.""HireDate"" AT TIME ZONE 'UTC')::date - 1
   AND NOT EXISTS (SELECT 1 FROM ""EmployeeEmploymentPeriods"" o
                    WHERE o.""EmployeeId"" = p.""EmployeeId"" AND o.""Id"" <> p.""Id"" AND o.""StartDate"" = p.""StartDate"" + 1);

UPDATE ""EmployeeEmploymentPeriods"" p
   SET ""EndDate"" = p.""EndDate"" + 1
  FROM ""Employees"" e
 WHERE e.""Id"" = p.""EmployeeId""
   AND e.""TerminationDate"" IS NOT NULL
   AND p.""EndDate"" = (e.""TerminationDate"" AT TIME ZONE 'UTC')::date - 1;

UPDATE ""EmployeeScheduleAssignments"" a
   SET ""EffectiveFrom"" = a.""EffectiveFrom"" + 1
  FROM ""Employees"" e
 WHERE e.""Id"" = a.""EmployeeId""
   AND a.""EffectiveFrom"" = (e.""HireDate"" AT TIME ZONE 'UTC')::date - 1
   AND NOT EXISTS (SELECT 1 FROM ""EmployeeScheduleAssignments"" o
                    WHERE o.""EmployeeId"" = a.""EmployeeId"" AND o.""Id"" <> a.""Id"" AND o.""EffectiveFrom"" = a.""EffectiveFrom"" + 1);

UPDATE ""EmployeeStatusPeriods"" s
   SET ""StartDate"" = s.""StartDate"" + 1
  FROM ""Employees"" e
 WHERE e.""Id"" = s.""EmployeeId""
   AND s.""StartDate"" = (e.""HireDate"" AT TIME ZONE 'UTC')::date - 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
