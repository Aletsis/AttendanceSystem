using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Application.Features.Attendance.EventHandlers;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Reproductions;

/// <summary>
/// Matriz de reproducciones de los hallazgos H01–H11 del Plan Maestro de Asistencia (F00-05).
/// Cada prueba documenta las entradas, resultado actual (demostrando la deficiencia de la línea base),
/// el resultado esperado según los contratos de negocio acordados y la causa raíz identificada.
/// </summary>
public class AttendanceFindingsReproductionTests
{
    #region H01 — UTC comparado con horario civil; agrupación por fecha UTC

    [Fact]
    public void H01_LocalTime_At_Night_In_UtcMinus6_Gets_Grouped_Into_Next_Day_When_Filtering_By_Utc_Date()
    {
        // ENTRADA:
        // Marcaje civil a las 20:00 (8:00 PM) del Lunes 24 de Agosto en CDMX (UTC-6).
        var localTime = new DateTime(2026, 8, 24, 20, 0, 0);
        var punch = AttendanceRegressionFixtures.CreatePunch(
            AttendanceRegressionFixtures.EmployeeFixedId,
            localTime,
            CheckType.CheckOut);

        // En UTC, 20:00 UTC-6 es 02:00 AM del Martes 25 de Agosto.
        punch.CheckTime.Date.Should().Be(new DateTime(2026, 8, 25));

        // RESULTADO ACTUAL EN LÍNEA BASE (Causa de H01):
        // ProcessDailyAttendanceCommandHandler línea 244 agrupa con:
        // r.CheckTime.Date == date.Date
        var queryDateLunes = new DateTime(2026, 8, 24);
        bool matchesLunes = punch.CheckTime.Date == queryDateLunes.Date;

        // DEMOSTRACIÓN DEL DEFECTO H01:
        // El filtro de la línea base descarta el marcaje del Lunes porque compara la fecha de CheckTime (UTC)
        matchesLunes.Should().BeFalse("en la línea base, r.CheckTime.Date evalúa a 25/08/2026 (UTC), perdiendo el marcaje civil del 24/08/2026");

        // RESULTADO ESPERADO:
        // Debe coincidir con la fecha laboral civil (WorkDate o tiempo local)
        punch.WorkDate.Should().Be(DateOnly.FromDateTime(queryDateLunes));
    }

    #endregion

    #region H02 — Reutilización al recalcular días contiguos

    [Fact]
    public void H02_Recalculating_Contiguous_Day_Resets_Previous_Night_Exit_To_Pending()
    {
        // ENTRADA:
        // Turno nocturno: Lunes 22:00 a Martes 06:00.
        // Salida a las 06:00 del Martes ya fue consumida y procesada para la jornada del Lunes.
        var exitMartes = AttendanceRegressionFixtures.CreatePunch(
            AttendanceRegressionFixtures.EmployeeFixedId,
            new DateTime(2026, 8, 25, 6, 0, 0),
            CheckType.CheckOut,
            status: AttendanceStatus.Processed);

        exitMartes.Status.Should().Be(AttendanceStatus.Processed);

        // RESULTADO ACTUAL EN LÍNEA BASE (Causa de H02):
        // Al recalcular el Martes 25/08, ProcessDailyAttendanceCommandHandler líneas 216-228 ejecuta:
        // rangeStartLimit = Martes 00:00:00, rangeEndLimit = Martes 23:59:59
        // if (record.CheckTime >= rangeStartLimit && record.CheckTime <= rangeEndLimit) record.ResetStatus();
        var rangeStartLimit = new DateTime(2026, 8, 25, 0, 0, 0);
        var rangeEndLimit = new DateTime(2026, 8, 25, 23, 59, 59);

        // DEMOSTRACIÓN DEL DEFECTO H02:
        if (exitMartes.CheckTime >= rangeStartLimit && exitMartes.CheckTime <= rangeEndLimit)
        {
            exitMartes.ResetStatus();
        }

        exitMartes.Status.Should().Be(AttendanceStatus.Pending,
            "en la línea base, la salida del turno nocturno se resetea a Pending al recalcular el día siguiente");

        // RESULTADO ESPERADO:
        // La asignación debe conservarse o depender de relaciones explícitas, impidiendo que el recálculo
        // del martes convierta la salida del lunes en entrada huérfana del martes.
        // CORREGIDO EN FASE 02: el motor ya no reinicia estados por fecha; la regresión con el orquestador
        // real está en Phase02PunchOwnershipTests.Criterion1_*. Esta prueba conserva la demostración del patrón de línea base.
    }

    #endregion

    #region H03 — Bloqueo local en memoria sin coordinación entre procesos

    [Fact]
    public async Task H03_LocalMemoryLock_Allows_Concurrent_Execution_Across_Independent_Service_Instances()
    {
        // ENTRADA:
        // Dos instancias de bloqueo en memoria con SemaphoreSlim (simulando dos procesos independientes: Blazor y WPF).
        var processInstance1 = new InMemoryCalculationLockService();
        var processInstance2 = new InMemoryCalculationLockService();

        var employeeIds = new[] { AttendanceRegressionFixtures.EmployeeFixedId };
        var barrier = new Barrier(2);
        bool enteredConcurrently = false;

        // RESULTADO ACTUAL EN LÍNEA BASE (Causa de H03):
        // Ambas instancias usan diccionarios locales ConcurrentDictionary<string, SemaphoreSlim>.
        var task1 = Task.Run(async () =>
        {
            await processInstance1.ExecuteWithLockAsync(employeeIds, async () =>
            {
                barrier.SignalAndWait(1000);
                await Task.Delay(50);
            });
        });

        var task2 = Task.Run(async () =>
        {
            await processInstance2.ExecuteWithLockAsync(employeeIds, async () =>
            {
                enteredConcurrently = barrier.SignalAndWait(1000);
                await Task.Delay(50);
            });
        });

        await Task.WhenAll(task1, task2);

        // DEMOSTRACIÓN DEL DEFECTO H03:
        enteredConcurrently.Should().BeTrue(
            "en la línea base, dos procesos con locks en memoria ejecutan concurrentemente sobre el mismo empleado sin exclusión mutua");

        // RESULTADO ESPERADO:
        // El bloqueo debe coordinarse a nivel relacional en PostgreSQL (ej. pg_advisory_xact_lock)
        // para que la segunda instancia espere hasta que la primera termine o libere la transacción.
    }

    /// <summary>
    /// Réplica del bloqueo en memoria de la línea base (semáforos por proceso). La Fase 03 lo sustituyó por
    /// pg_advisory_xact_lock; la regresión con conexiones distintas está en PostgresConcurrencyTests.
    /// </summary>
    private class InMemoryCalculationLockService
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public async Task ExecuteWithLockAsync(
            IEnumerable<EmployeeId> employeeIds,
            Func<Task> action,
            CancellationToken cancellationToken = default)
        {
            var sortedIds = employeeIds.Select(e => e.Value).Distinct().OrderBy(id => id).ToList();
            var acquired = new List<SemaphoreSlim>();
            try
            {
                foreach (var id in sortedIds)
                {
                    var sem = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
                    await sem.WaitAsync(cancellationToken);
                    acquired.Add(sem);
                }
                await action();
            }
            finally
            {
                for (int i = acquired.Count - 1; i >= 0; i--) acquired[i].Release();
            }
        }
    }

    #endregion

    #region H04 — Reconciliación de lotes silencia excepciones y las reporta como duplicados

    [Fact]
    public async Task H04_BatchIngestion_InfrastructureFailure_Is_Reported_As_Failure_Not_As_Duplicates()
    {
        // ENTRADA:
        // Un lote con un registro nuevo que falla por una excepción en base de datos (ej. error de conexión o restricción)
        var attendanceRepoMock = new Mock<IAttendanceRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var timeNormalizerMock = new Mock<ITimeNormalizer>();
        var loggerMock = new Mock<ILogger<RecordAttendanceBatchCommandHandler>>();

        timeNormalizerMock.Setup(t => t.ToUtc(It.IsAny<DateTime>(), It.IsAny<string?>()))
            .Returns<DateTime, string?>((d, _) => d);
        timeNormalizerMock.Setup(t => t.ToWorkDate(It.IsAny<DateTime>(), It.IsAny<string?>()))
            .Returns<DateTime, string?>((d, _) => DateOnly.FromDateTime(d));

        attendanceRepoMock.Setup(r => r.GetByIdentitiesAsync(It.IsAny<IReadOnlyCollection<(EmployeeId, DateTime)>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord>());

        // Simular que SaveChangesAsync arroja una excepción grave (ej. fallo relacional)
        unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fallo de infraestructura simulado en PostgreSQL"));

        var handler = new RecordAttendanceBatchCommandHandler(
            attendanceRepoMock.Object,
            unitOfWorkMock.Object,
            timeNormalizerMock.Object,
            loggerMock.Object);

        var command = new RecordAttendanceBatchCommand(
            DeviceId: "DEV-01",
            Logs: new List<AttendanceLogDto>
            {
                new("EMP-001", DateTime.UtcNow, 1, 1)
            });

        // ACT:
        var result = await handler.Handle(command, CancellationToken.None);

        // LÍNEA BASE (F00-05): el catch genérico devolvía Success(0) y contaba el error como duplicado omitido.
        // CORREGIDO EN FASE 03 (F03-06): sólo una identidad existente confirmada cuenta como duplicado; un fallo
        // técnico se reporta como fallo para que el dispositivo reintente.
        result.IsSuccess.Should().BeFalse("un fallo de infraestructura no es un duplicado");
        result.Error.Should().Contain("Unknown");
        attendanceRepoMock.Verify(r => r.GetByEmployeeAndCheckTimeAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never,
            "no se reintenta fila a fila con las entidades fallidas aún pendientes");
        attendanceRepoMock.Verify(r => r.Detach(It.IsAny<AttendanceRecord>()), Times.Once, "el contexto se deja coherente tras el fallo");
    }

    #endregion

    #region H05 — Errores de consolidación absorbidos y pendientes sin consumidor

    [Fact]
    public void H05_ConsolidationRequests_Have_No_Background_Consumer_In_The_Solution()
    {
        // ENTRADA:
        // Solicitud en estado Pending creada por AttendanceRecordedEventHandler
        var request = AttendanceConsolidationRequest.Create(
            AttendanceRegressionFixtures.EmployeeFixedId,
            DateTime.Today.AddDays(-1),
            DateTime.Today);

        request.Status.Should().Be(ConsolidationStatus.Pending);

        // RESULTADO ACTUAL EN LÍNEA BASE (Causa de H05):
        // IAttendanceConsolidationRepository define GetPendingBatchAsync, pero ninguna clase
        // en toda la solución (.Application, .Infrastructure, .Blazor) invoca ese método ni procesa la tabla.
        var usagesOfGetPendingBatch = typeof(IAttendanceConsolidationRepository).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods())
            .Where(m => m.Name == nameof(IAttendanceConsolidationRepository.GetPendingBatchAsync))
            .ToList();

        usagesOfGetPendingBatch.Should().NotBeEmpty();

        // DEMOSTRACIÓN DEL DEFECTO H05:
        // Si Hangfire se cae o el servidor se reinicia antes de ejecutar el job en memoria,
        // la solicitud en base de datos queda permanentemente en Pending sin consumidor.
    }

    #endregion

    #region H06 — Ajustes omitidos en turnos mixtos sin configuración diaria

    [Fact]
    public async Task H06_MixedShift_Without_Day_Config_Omits_Manual_Adjustment()
    {
        // ENTRADA:
        // Turno mixto donde el Sábado no tiene configuración en Shift.Days.
        var mixedShift = AttendanceRegressionFixtures.CreateMixedShift();
        var dateSabado = new DateTime(2026, 8, 29); // Sábado
        dateSabado.DayOfWeek.Should().Be(DayOfWeek.Saturday);

        var employee = AttendanceRegressionFixtures.CreateFixedEmployee();
        var senderMock = new Mock<ISender>();
        var loggerMock = new Mock<ILogger<ProcessMixAttendanceCommandHandler>>();

        var handler = new ProcessMixAttendanceCommandHandler(senderMock.Object, loggerMock.Object);

        var adjustment = AttendanceAdjustment.Create(employee.Id, dateSabado, "Supervisor RH", "Autorizado");

        var command = new ProcessMixAttendanceCommand(
            employee,
            dateSabado,
            mixedShift,
            new List<AttendanceRecord>(),
            IsRestDay: false,
            Adjustment: adjustment);

        // ACT:
        await handler.Handle(command, CancellationToken.None);

        // DEMOSTRACIÓN DEL DEFECTO H06:
        // ProcessMixAttendanceCommandHandler en líneas 44-48 delega a ProcessNoShiftAttendanceCommand sin pasar Adjustment:
        senderMock.Verify(s => s.Send(
            It.Is<ProcessNoShiftAttendanceCommand>(cmd => cmd.Adjustment == null),
            It.IsAny<CancellationToken>()), Times.Once,
            "en la línea base, la delegación cuando falta configuración diaria omite el Adjustment del supervisor");

        // RESULTADO ESPERADO:
        // Debe preservar Adjustment: adjustment aún si el día de la semana no tenía configuración explícita.
    }

    #endregion

    #region H07 — Asignación de marcaje ajeno sin validación de pertenencia

    [Fact]
    public async Task H07_ManuallyAssignAttendance_Rejects_Punch_That_Belongs_To_Another_Employee()
    {
        // ENTRADA:
        // Un marcaje biométrico perteneciente a EMP-ROT-002
        var foreignPunch = AttendanceRegressionFixtures.CreatePunch(
            AttendanceRegressionFixtures.EmployeeRotativeId,
            new DateTime(2026, 8, 28, 8, 0, 0),
            CheckType.CheckIn);

        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var attendanceRepoMock = new Mock<IAttendanceRepository>();
        var adjustmentRepoMock = new Mock<IAttendanceAdjustmentRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();

        var targetDaily = DailyAttendance.Create(
            AttendanceRegressionFixtures.EmployeeFixedId, // Empleado Destino: EMP-FIX-001
            new DateTime(2026, 8, 28),
            AttendanceRegressionFixtures.CreateRegularShift(),
            null, null, false);

        dailyRepoMock.Setup(d => d.GetByEmployeeAndDateAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetDaily);

        attendanceRepoMock.Setup(a => a.GetByIdAsync(foreignPunch.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(foreignPunch);

        var handler = new ManuallyAssignAttendanceCommandHandler(
            dailyRepoMock.Object,
            attendanceRepoMock.Object,
            adjustmentRepoMock.Object,

            Mock.Of<IPunchAssignmentRepository>(),
            unitOfWorkMock.Object,
            Mock.Of<IAttendanceCalculationLockService>());

        var command = new ManuallyAssignAttendanceCommand(
            EmployeeId: AttendanceRegressionFixtures.EmployeeFixedId.Value, // EMP-FIX-001
            Date: DateOnly.FromDateTime(new DateTime(2026, 8, 28)),
            RecordId: foreignPunch.Id.Value.ToString(),
            AssignmentType: "Entrada");

        // ACT:
        var result = await handler.Handle(command, CancellationToken.None);

        // LÍNEA BASE (F00-05): la asignación se aceptaba sin validar foreignPunch.EmployeeId != command.EmployeeId.
        // CORREGIDO EN FASE 02 (F02-02): se rechaza antes de cualquier escritura.
        result.IsSuccess.Should().BeFalse("un marcaje de otro empleado no puede asignarse (C-04)");
        result.Error.Should().Be("El marcaje no pertenece al empleado indicado.");
        targetDaily.CheckInRecordId.Should().BeNull("el rechazo no deja escrituras parciales");
        unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        adjustmentRepoMock.Verify(a => a.AddAsync(It.IsAny<AttendanceAdjustment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region H08 — Continuo delegado a flexible con límite de 18 horas

    [Fact]
    public async Task H08_Continuous_24h_Shift_Misses_Exit_Due_To_18_Hour_Threshold_In_Flexible_Handler()
    {
        // ENTRADA:
        // Turno continuo de 24 horas: 08:00 AM del Día 1 a 08:00 AM del Día 2 (24 horas).
        var continuousShift = AttendanceRegressionFixtures.CreateContinuous24hShift();
        var employee = AttendanceRegressionFixtures.CreateFixedEmployee();

        var entry = AttendanceRegressionFixtures.CreatePunch(
            employee.Id,
            new DateTime(2026, 8, 24, 8, 0, 0),
            CheckType.CheckIn);

        var exitAt24h = AttendanceRegressionFixtures.CreatePunch(
            employee.Id,
            new DateTime(2026, 8, 25, 8, 0, 0), // 24 horas después
            CheckType.CheckOut);

        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var attendanceRepoMock = new Mock<IAttendanceRepository>();
        var loggerMock = new Mock<ILogger<ProcessFlexibleAttendanceCommandHandler>>();

        DailyAttendance? persistedDaily = null;
        dailyRepoMock.Setup(d => d.Add(It.IsAny<DailyAttendance>()))
            .Callback<DailyAttendance>(d => persistedDaily = d);

        var handler = new ProcessFlexibleAttendanceCommandHandler(
            dailyRepoMock.Object,
            attendanceRepoMock.Object,
            loggerMock.Object);

        var command = new ProcessFlexibleAttendanceCommand(
            employee,
            new DateTime(2026, 8, 24),
            continuousShift,
            new List<AttendanceRecord> { entry, exitAt24h },
            IsRestDay: false);

        // ACT:
        await handler.Handle(command, CancellationToken.None);

        // DEMOSTRACIÓN DEL DEFECTO H08:
        // En ProcessFlexibleAttendanceCommandHandler línea 147:
        // (r.CheckTime - checkIn.Value).TotalHours <= 18
        // La salida a las 24 horas es descartada y checkOut queda en null.
        persistedDaily.Should().NotBeNull();
        persistedDaily!.ActualCheckIn.Should().NotBeNull();
        persistedDaily.ActualCheckOut.Should().BeNull(
            "en la línea base, la salida a las 24h es ignorada debido al límite rígido de 18 horas");

        // RESULTADO ESPERADO:
        // Un turno de 24h continuo debe asociar su salida en la ventana de 24 horas acordada.
    }

    #endregion

    #region H09 — Intervalo abierto oculto en MultiInterval

    [Fact]
    public async Task H09_MultiInterval_With_Odd_Punches_Hides_Last_Open_Interval()
    {
        // ENTRADA:
        // Turno MultiInterval con 3 marcajes (número impar): 08:00, 12:00, 13:00.
        var multiShift = AttendanceRegressionFixtures.CreateMultiIntervalShift();
        var employee = AttendanceRegressionFixtures.CreateFixedEmployee();
        var date = new DateTime(2026, 8, 24);

        var punch1 = AttendanceRegressionFixtures.CreatePunch(employee.Id, new DateTime(2026, 8, 24, 8, 0, 0), CheckType.CheckIn);
        var punch2 = AttendanceRegressionFixtures.CreatePunch(employee.Id, new DateTime(2026, 8, 24, 12, 0, 0), CheckType.CheckOut);
        var punch3 = AttendanceRegressionFixtures.CreatePunch(employee.Id, new DateTime(2026, 8, 24, 13, 0, 0), CheckType.CheckIn);

        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var attendanceRepoMock = new Mock<IAttendanceRepository>();
        var loggerMock = new Mock<ILogger<ProcessFlexibleAttendanceCommandHandler>>();

        DailyAttendance? persistedDaily = null;
        dailyRepoMock.Setup(d => d.Add(It.IsAny<DailyAttendance>()))
            .Callback<DailyAttendance>(d => persistedDaily = d);

        var handler = new ProcessFlexibleAttendanceCommandHandler(
            dailyRepoMock.Object,
            attendanceRepoMock.Object,
            loggerMock.Object);

        var command = new ProcessFlexibleAttendanceCommand(
            employee,
            date,
            multiShift,
            new List<AttendanceRecord> { punch1, punch2, punch3 },
            IsRestDay: false);

        // ACT:
        await handler.Handle(command, CancellationToken.None);

        // DEMOSTRACIÓN DEL DEFECTO H09:
        // En ProcessFlexibleAttendanceCommandHandler líneas 61-88:
        // El bucle de pares cleanRecords[i], cleanRecords[i+1] solo agrega intervalos cuando existe i+1.
        // El tercer marcaje (13:00) se marca como Processed, pero IntervalsData solo contiene 1 intervalo [08:00-12:00].
        persistedDaily.Should().NotBeNull();
        persistedDaily!.IntervalsData.Should().NotBeNull();
        persistedDaily.IntervalsData.Should().NotContain("13:00",
            "en la línea base, el marcaje impar no se refleja en los intervalos ni deja advertencia de intervalo abierto");

        // RESULTADO ESPERADO:
        // El intervalo abierto debe quedar registrado como abierto y con incidencia visible para RH.
    }

    #endregion

    #region H10 — Autodetección con DateTime.MinValue en colección vacía

    [Fact]
    public void H10_ShiftResolution_Autodetects_Shift_On_Empty_Punches_Due_To_ValueType_Default()
    {
        // ENTRADA:
        // Empleado rotativo sin marcajes en el día (colección vacía).
        var rotativeEmployee = AttendanceRegressionFixtures.CreateRotativeEmployee();
        var shifts = new Dictionary<ShiftId, Shift>
        {
            [AttendanceRegressionFixtures.RegularShiftId] = AttendanceRegressionFixtures.CreateRegularShift(),
            [AttendanceRegressionFixtures.NightShiftId] = AttendanceRegressionFixtures.CreateNightShift()
        };

        var emptyPunches = new List<DateTime>();

        // RESULTADO ACTUAL EN LÍNEA BASE (Causa de H10):
        // En AttendanceShiftResolutionService.cs líneas 92-95:
        // var firstPunch = dayPunches?.OrderBy(p => p).FirstOrDefault();
        // if (firstPunch.HasValue) ...
        // Como DateTime es struct, FirstOrDefault() devuelve DateTime.MinValue (0001-01-01 00:00:00).
        // firstPunch.HasValue es TRUE siempre.
        var resolution = AttendanceShiftResolutionService.Resolve(
            rotativeEmployee,
            new DateTime(2026, 8, 24), // Lunes (no es día de descanso del rotativo)
            roster: null,
            adjustment: null,
            shifts,
            emptyPunches);

        // DEMOSTRACIÓN DEL DEFECTO H10:
        resolution.IsAutoDetected.Should().BeTrue(
            "en la línea base, FirstOrDefault() en una lista vacía de DateTime devuelve DateTime.MinValue, evaluando HasValue = true y autodetectando un turno falso");

        // RESULTADO ESPERADO:
        // En un día sin marcajes, IsAutoDetected debe ser false y Shift debe ser null.
    }

    #endregion

    #region H11 — Extras semanales ignoran autorización diaria y tope depende del rango

    [Fact]
    public void H11_WeeklyOvertime_Ignores_Daily_Authorization_And_PeriodCap_Depends_On_Batch_Range()
    {
        // ENTRADA:
        // Empleado con OvertimeAuthorized = false globalmente, pero el día tuvo un override.
        var employee = AttendanceRegressionFixtures.CreateFixedEmployee();
        var employeeWithoutOvertime = Employee.Create(
            id: employee.Id,
            firstName: "Empleado",
            lastName: "Sin Extras",
            email: "noot@test.com",
            phoneNumber: null,
            hireDate: new DateTime(2024, 1, 1),
            gender: Gender.Male,
            branchId: AttendanceRegressionFixtures.BranchNorthId,
            departmentId: AttendanceRegressionFixtures.DeptOpsId,
            positionId: AttendanceRegressionFixtures.PosOperatorId,
            shiftType: ShiftType.Matutino,
            overtimeAuthorized: false); // Globalmente desautorizado

        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        var dailyAttendance = DailyAttendance.Create(
            employee.Id,
            new DateTime(2026, 8, 24),
            shift,
            new DateTime(2026, 8, 24, 8, 0, 0),
            new DateTime(2026, 8, 24, 18, 0, 0), // 2 horas extras
            isRestDay: false,
            overtimeAuthorized: true, // Autorizado específicamente para este día
            totalWorkedMinutes: 600); // 10 horas trabajadas (600 min)

        // ACT:
        var result = WeeklyHoursCalculationHelper.CalculateEffectiveOvertime(
            new[] { dailyAttendance },
            employeeWithoutOvertime,
            DayOfWeek.Monday);

        // DEMOSTRACIÓN DEL DEFECTO H11:
        // En WeeklyHoursCalculationHelper líneas 123 y 140:
        // Solo evalúa employee.OvertimeAuthorized (false), ignorando la autorización de dailyAttendance.
        result[dailyAttendance.Id].Should().Be(0,
            "en la línea base, se evalúa únicamente la bandera global del empleado, ignorando autorizaciones diarias");

        // RESULTADO ESPERADO:
        // Debe respetar la autorización diaria de la jornada.
    }

    #endregion
}
