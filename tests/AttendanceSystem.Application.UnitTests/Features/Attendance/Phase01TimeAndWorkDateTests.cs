using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Application.Features.Attendance.Services;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

/// <summary>
/// Pruebas automatizadas de la Fase 01 — Tiempo, zonas y fecha laboral (F01-01 a F01-07).
/// Verifica los 6 criterios de aceptación definidos en FASE-01-Tiempo.md:
/// 1. Entrada civil 08:00 en UTC−6 contra turno 08:00: cero retardo, independiente del servidor.
/// 2. Marcaje civil 23:30: su almacenamiento UTC al día siguiente no cambia por sí solo la fecha laboral.
/// 3. Jornadas nocturnas y límites exactos no pierden ni duplican marcajes entre consultas [inicio, fin).
/// 4. Hora ambigua, inexistente y zona inválida producen el resultado definido y dejan evidencia.
/// 5. Reingresar datos normalizados no aplica una segunda conversión (idempotencia de normalización).
/// 6. Simular el diagnóstico / migración dos veces produce 0 pendientes en la segunda ejecución.
/// </summary>
public class Phase01TimeAndWorkDateTests
{
    private const string MexicoCityZone = "America/Mexico_City";
    private readonly ITimeNormalizer _normalizer = DefaultTimeNormalizer.Instance;
    private readonly IAuthoritativeTimeZoneResolver _zoneResolver = DefaultAuthoritativeTimeZoneResolver.Instance;

    #region Criterio 1: Entrada civil 08:00 en UTC−6 contra turno 08:00 -> 0 retardo

    [Fact]
    public async Task Criterion1_CivilPunch0800_InUtcMinus6_Against0800Shift_YieldsZeroLateMinutes_RegardlessOfServerZone()
    {
        // ARRANGE:
        // Turno regular de 08:00 a 16:00 con 10 minutos de tolerancia
        var shift = Shift.Create(
            name: "Matutino 08-16",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 10,
            workHours: TimeSpan.FromHours(8),
            shiftType: ShiftType.Matutino);

        var employee = AttendanceRegressionFixtures.CreateFixedEmployee();
        var punchDate = new DateTime(2026, 8, 24);

        // Marcaje civil exactamente a las 08:00:00 (en horario de entrada) en zona UTC-6
        var civilCheckIn = punchDate.AddHours(8); // 2026-08-24 08:00:00
        var utcCheckIn = _normalizer.ToUtc(civilCheckIn, MexicoCityZone); // 2026-08-24 14:00:00 UTC

        utcCheckIn.Kind.Should().Be(DateTimeKind.Utc);
        utcCheckIn.Hour.Should().Be(14, "porque CDMX en agosto está en UTC-6");

        var punchRecord = AttendanceRecord.Create(
            employeeId: employee.Id,
            deviceId: DeviceId.From("DEV-01"),
            checkTime: utcCheckIn,
            verifyMethod: VerifyMethod.Fingerprint,
            checkType: CheckType.CheckIn,
            workDate: DateOnly.FromDateTime(civilCheckIn),
            originalCheckTime: civilCheckIn,
            timeZoneId: MexicoCityZone,
            source: "BiometricDevice");

        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var attendanceRepoMock = new Mock<IAttendanceRepository>();
        DailyAttendance? savedDaily = null;

        dailyRepoMock.Setup(r => r.Add(It.IsAny<DailyAttendance>()))
            .Callback<DailyAttendance>(da => savedDaily = da);

        var handler = new ProcessRegularAttendanceCommandHandler(
            dailyRepoMock.Object,
            attendanceRepoMock.Object,
            NullLogger<ProcessRegularAttendanceCommandHandler>.Instance);

        var command = new ProcessRegularAttendanceCommand(
            Employee: employee,
            Date: punchDate,
            Shift: shift,
            Records: new List<AttendanceRecord> { punchRecord },
            IsRestDay: false,
            TimeZoneId: MexicoCityZone);

        // ACT:
        await handler.Handle(command, CancellationToken.None);

        // ASSERT:
        savedDaily.Should().NotBeNull();
        savedDaily!.ActualCheckIn.Should().Be(utcCheckIn, "conserva el instante UTC persistido");
        savedDaily.CivilCheckIn.Should().Be(civilCheckIn, "interpreta correctamente la hora civil local");
        savedDaily.LateMinutes.Should().Be(0, "porque la hora civil 08:00 es puntual respecto al turno de las 08:00");
        savedDaily.IsAbsent.Should().BeFalse();
    }

    #endregion

    #region Criterio 2: Marcaje civil 23:30 almacenado a las 05:30 UTC siguiente día preserva WorkDate

    [Fact]
    public void Criterion2_CivilPunch2330_StoredAt0530UtcNextDay_PreservesWorkDate()
    {
        // ARRANGE:
        // Marcaje civil del Lunes a las 23:30 (11:30 PM) en CDMX (UTC-6)
        var localPunchCivil = new DateTime(2026, 8, 24, 23, 30, 0, DateTimeKind.Unspecified);
        var expectedWorkDate = new DateOnly(2026, 8, 24);

        // ACT:
        var utcTime = _normalizer.ToUtc(localPunchCivil, MexicoCityZone);
        var calculatedWorkDate = _normalizer.ToWorkDate(utcTime, MexicoCityZone);

        var record = AttendanceRecord.Create(
            employeeId: EmployeeId.From("EMP-001"),
            deviceId: DeviceId.From("DEV-01"),
            checkTime: utcTime,
            verifyMethod: VerifyMethod.Fingerprint,
            checkType: CheckType.CheckOut,
            workDate: calculatedWorkDate,
            originalCheckTime: localPunchCivil,
            timeZoneId: MexicoCityZone);

        // ASSERT:
        // En UTC, 23:30 UTC-6 es 05:30 del Martes 25 de Agosto
        utcTime.Date.Should().Be(new DateTime(2026, 8, 25));
        utcTime.Hour.Should().Be(5);
        utcTime.Minute.Should().Be(30);

        // Sin embargo, la fecha laboral (WorkDate) y civil permanecen en Lunes 24 de Agosto
        record.WorkDate.Should().Be(expectedWorkDate);
        record.GetWorkDate().Should().Be(expectedWorkDate);
        record.GetCivilTime().Should().Be(localPunchCivil);
        record.GetCivilTime().Date.Should().Be(new DateTime(2026, 8, 24));
    }

    #endregion

    #region Criterio 3: Límites semiabiertos [inicio, fin) y jornadas nocturnas

    [Fact]
    public void Criterion3_SemiOpenInterval_DoesNotDuplicateOrDropBoundaryPunches()
    {
        // ARRANGE:
        var day1 = new DateOnly(2026, 8, 24);
        var day2 = new DateOnly(2026, 8, 25);
        var day3 = new DateOnly(2026, 8, 26);

        // Ventana para Day 1: [2026-08-24 00:00:00, 2026-08-25 00:00:00)
        var (d1StartUtc, d1EndUtc) = _normalizer.GetCivilWindowInUtc(day1, day1, MexicoCityZone);

        // Ventana para Day 2: [2026-08-25 00:00:00, 2026-08-26 00:00:00)
        var (d2StartUtc, d2EndUtc) = _normalizer.GetCivilWindowInUtc(day2, day2, MexicoCityZone);

        // Marcaje civil exactamente en la medianoche frontera: 2026-08-25 00:00:00
        var boundaryPunchCivil = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Unspecified);
        var boundaryPunchUtc = _normalizer.ToUtc(boundaryPunchCivil, MexicoCityZone);

        // ACT & ASSERT:
        // En intervalo semiabierto [inicio, fin), el límite superior es EXCLUSIVO:
        bool inDay1Window = boundaryPunchUtc >= d1StartUtc && boundaryPunchUtc < d1EndUtc;
        bool inDay2Window = boundaryPunchUtc >= d2StartUtc && boundaryPunchUtc < d2EndUtc;

        inDay1Window.Should().BeFalse("porque d1EndUtc es límite superior exclusivo");
        inDay2Window.Should().BeTrue("porque d2StartUtc es límite inferior inclusivo");

        // Garantía de no duplicidad y no pérdida:
        (inDay1Window ^ inDay2Window).Should().BeTrue("el marcaje frontera pertenece a exactamente una ventana semiabierta");
    }

    [Fact]
    public async Task Criterion3_NightShift_AssociatesBothPunchesToStartingWorkDate()
    {
        // ARRANGE:
        // Turno nocturno: 22:00 a 06:00 del día siguiente
        var nightShift = AttendanceRegressionFixtures.CreateNightShift();
        var employee = AttendanceRegressionFixtures.CreateFixedEmployee();
        var workDate = new DateTime(2026, 8, 24);

        // Entrada: 22:00 del 24 de agosto
        var entryCivil = new DateTime(2026, 8, 24, 22, 0, 0);
        var entryUtc = _normalizer.ToUtc(entryCivil, MexicoCityZone);
        var entryRecord = AttendanceRecord.Create(
            employee.Id, DeviceId.From("DEV-01"), entryUtc, VerifyMethod.Fingerprint, CheckType.CheckIn,
            workDate: DateOnly.FromDateTime(entryCivil), originalCheckTime: entryCivil, timeZoneId: MexicoCityZone);

        // Salida: 06:00 del 25 de agosto (madrugada del día siguiente)
        var exitCivil = new DateTime(2026, 8, 25, 6, 0, 0);
        var exitUtc = _normalizer.ToUtc(exitCivil, MexicoCityZone);
        var exitRecord = AttendanceRecord.Create(
            employee.Id, DeviceId.From("DEV-01"), exitUtc, VerifyMethod.Fingerprint, CheckType.CheckOut,
            workDate: DateOnly.FromDateTime(entryCivil), // Asociado a la jornada nocturna iniciada el 24
            originalCheckTime: exitCivil, timeZoneId: MexicoCityZone);

        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var attendanceRepoMock = new Mock<IAttendanceRepository>();
        DailyAttendance? savedDaily = null;

        dailyRepoMock.Setup(r => r.Add(It.IsAny<DailyAttendance>()))
            .Callback<DailyAttendance>(da => savedDaily = da);

        var handler = new ProcessNightlyAttendanceCommandHandler(
            dailyRepoMock.Object,
            attendanceRepoMock.Object,
            NullLogger<ProcessNightlyAttendanceCommandHandler>.Instance);

        var command = new ProcessNightlyAttendanceCommand(
            Employee: employee,
            Date: workDate,
            Shift: nightShift,
            Records: new List<AttendanceRecord> { entryRecord, exitRecord },
            IsRestDay: false,
            DayStartTime: nightShift.StartTime,
            DayEndTime: nightShift.EndTime,
            TimeZoneId: MexicoCityZone);

        // ACT:
        await handler.Handle(command, CancellationToken.None);

        // ASSERT:
        savedDaily.Should().NotBeNull();
        savedDaily!.ActualCheckIn.Should().Be(entryUtc);
        savedDaily.ActualCheckOut.Should().Be(exitUtc);
        savedDaily.CivilCheckIn.Should().Be(entryCivil);
        savedDaily.CivilCheckOut.Should().Be(exitCivil);
        savedDaily.LateMinutes.Should().Be(0);
        savedDaily.EarlyDepartureMinutes.Should().Be(0);
        savedDaily.IsAbsent.Should().BeFalse();
        savedDaily.TotalWorkedMinutes.Should().Be(480, "8 horas trabajadas (22:00 a 06:00)");
    }

    #endregion

    #region Criterio 4: Hora ambigua, inexistente y zona inválida producen resultado determinista y trazable

    [Fact]
    public void Criterion4_InvalidTimeZone_GracefullyFallsBackToDefaultWithoutCrashing()
    {
        // ARRANGE:
        var civilTime = new DateTime(2026, 8, 24, 8, 0, 0, DateTimeKind.Unspecified);
        var invalidZone = "Invalid/NonExistent_Zone_12345";

        // ACT:
        var utcTime = _normalizer.ToUtc(civilTime, invalidZone);
        var workDate = _normalizer.ToWorkDate(utcTime, invalidZone);

        // ASSERT:
        utcTime.Kind.Should().Be(DateTimeKind.Utc);
        utcTime.Hour.Should().Be(14, "aplica el fallback determinista a America/Mexico_City (UTC-6)");
        workDate.Should().Be(new DateOnly(2026, 8, 24));
    }

    [Fact]
    public void Criterion4_AmbiguousTime_And_GapTime_ProduceDeterministicConversions()
    {
        // Prueba con zona que tiene horario de verano (ej. Eastern Time "America/New_York"):
        // En America/New_York:
        // Salto adelante (gap/inexistente): 2026-03-08 a las 02:30 no existe (salta de 02:00 a 03:00)
        // Salto atrás (ambigua): 2026-11-01 a las 01:30 ocurre dos veces
        var nyZone = "America/New_York";
        var gapCivilTime = new DateTime(2026, 3, 8, 2, 30, 0, DateTimeKind.Unspecified);
        var ambiguousCivilTime = new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Unspecified);

        // ACT:
        var gapUtc = _normalizer.ToUtc(gapCivilTime, nyZone);
        var ambiguousUtc = _normalizer.ToUtc(ambiguousCivilTime, nyZone);

        // ASSERT:
        gapUtc.Kind.Should().Be(DateTimeKind.Utc);
        ambiguousUtc.Kind.Should().Be(DateTimeKind.Utc);

        // Conversión inversa consistente (trazabilidad y reproducibilidad):
        var gapCivilRoundTrip = _normalizer.ToCivilTime(gapUtc, nyZone);
        var ambCivilRoundTrip = _normalizer.ToCivilTime(ambiguousUtc, nyZone);

        gapCivilRoundTrip.Hour.Should().Be(3, "la hora inexistente avanza al intervalo válido siguiente (03:30)");
        ambCivilRoundTrip.Hour.Should().Be(1, "la hora ambigua resuelve de forma determinista");
    }

    #endregion

    #region Criterio 5: Reingresar datos normalizados no aplica una segunda conversión

    [Fact]
    public void Criterion5_NormalizingAlreadyUtcTimestamp_DoesNotApplySecondaryShift()
    {
        // ARRANGE:
        // Supongamos un instante ya normalizado a las 14:00 UTC
        var alreadyUtc = new DateTime(2026, 8, 24, 14, 0, 0, DateTimeKind.Utc);

        // ACT:
        // Normalizar un dato que ya es UTC no debe restar ni sumar 6 horas adicionales
        var resultUtc = _normalizer.ToUtc(alreadyUtc, MexicoCityZone);

        // ASSERT:
        resultUtc.Should().Be(alreadyUtc, "ToUtc sobre un instante UTC es idempotente y no aplica doble conversión");
        resultUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Criterion5_ReingestingExistingPunch_IsIdempotentAndDoesNotAlterMetadata()
    {
        // ARRANGE:
        var employeeId = EmployeeId.From("EMP-001");
        var deviceId = DeviceId.From("DEV-01");
        var civilTime = new DateTime(2026, 8, 24, 8, 0, 0);
        var utcCheckTime = _normalizer.ToUtc(civilTime, MexicoCityZone);

        var existingRecord = AttendanceRecord.Create(
            employeeId, deviceId, utcCheckTime, VerifyMethod.Fingerprint, CheckType.CheckIn,
            workDate: DateOnly.FromDateTime(civilTime), originalCheckTime: civilTime, timeZoneId: MexicoCityZone);

        var attendanceRepoMock = new Mock<IAttendanceRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();

        attendanceRepoMock.Setup(r => r.GetByIdentitiesAsync(
                It.Is<IReadOnlyCollection<(EmployeeId EmployeeId, DateTime CheckTimeUtc)>>(keys => keys.Any(k => k.EmployeeId == employeeId && k.CheckTimeUtc == utcCheckTime)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord> { existingRecord });

        var handler = new RecordAttendanceCommandHandler(
            attendanceRepoMock.Object,
            unitOfWorkMock.Object,
            _normalizer,
            _zoneResolver,
            NullLogger<RecordAttendanceCommandHandler>.Instance);

        var command = new RecordAttendanceCommand(
            EmployeeId: employeeId.Value,
            DeviceId: deviceId.Value,
            CheckTime: civilTime,
            VerifyMethodCode: VerifyMethod.Fingerprint.Id,
            CheckTypeCode: CheckType.CheckIn.Id,
            TimeZoneId: MexicoCityZone);

        // ACT:
        var result = await handler.Handle(command, CancellationToken.None);

        // ASSERT:
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(existingRecord.Id.Value, "retorna el ID existente en reingreso idéntico");
        attendanceRepoMock.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<AttendanceRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Criterio 6: Diagnóstico y migración ejecutados dos veces producen 0 pendientes en la segunda ejecución

    [Fact]
    public async Task Criterion6_HistoricalDiagnosticAndMigration_RunTwice_YieldsZeroPendingOnSecondExecution()
    {
        // ARRANGE:
        // Crear registros simulando histórico heredado con metadatos faltantes
        var legacyRecords = new List<AttendanceRecord>
        {
            // Registro heredado sin OriginalCheckTime ni WorkDate (ej. solo CheckTime = 14:00 UTC)
            AttendanceRecord.Create(
                EmployeeId.From("EMP-LEG-01"), DeviceId.From("DEV-01"),
                new DateTime(2026, 8, 24, 14, 0, 0, DateTimeKind.Utc),
                VerifyMethod.Fingerprint, CheckType.CheckIn),

            // Registro heredado 2
            AttendanceRecord.Create(
                EmployeeId.From("EMP-LEG-02"), DeviceId.From("DEV-01"),
                new DateTime(2026, 8, 24, 22, 0, 0, DateTimeKind.Utc),
                VerifyMethod.Fingerprint, CheckType.CheckOut)
        };

        var attendanceRepoMock = new Mock<IAttendanceRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();

        attendanceRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(legacyRecords);

        var diagnosticService = new HistoricalAttendanceTimeDiagnosticService(
            attendanceRepoMock.Object,
            _zoneResolver,
            _normalizer,
            unitOfWorkMock.Object,
            NullLogger<HistoricalAttendanceTimeDiagnosticService>.Instance);

        // ACT 1: Primera ejecución de Diagnóstico
        var firstReport = await diagnosticService.DiagnoseAsync(cancellationToken: CancellationToken.None);
        firstReport.TotalInspected.Should().Be(2);
        firstReport.PendingModificationsCount.Should().Be(2, "los 2 registros no tenían TimeZoneId ni OriginalCheckTime consistente");

        // ACT 2: Ejecución de Migración
        var migrationResult = await diagnosticService.MigrateAsync(dryRun: false, cancellationToken: CancellationToken.None);
        migrationResult.UpdatedRecordsCount.Should().Be(2);
        unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // ACT 3: Segunda ejecución de Diagnóstico sobre los registros migrados
        var secondReport = await diagnosticService.DiagnoseAsync(cancellationToken: CancellationToken.None);

        // ASSERT:
        secondReport.TotalInspected.Should().Be(2);
        secondReport.PendingModificationsCount.Should().Be(0, "en la segunda ejecución hay exactamente 0 registros pendientes de migración");
        secondReport.ReliableRecordsCount.Should().Be(2, "todos los registros cumplen con el contrato temporal");
        secondReport.AnomaliesRequiringHumanReview.Should().BeEmpty();
    }

    #endregion
}
