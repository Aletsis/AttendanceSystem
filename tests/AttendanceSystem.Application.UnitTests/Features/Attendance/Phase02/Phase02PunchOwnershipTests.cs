using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;

/// <summary>
/// Verificables de la Fase 02 — Pertenencia de marcajes y recálculo determinista (FASE-02-Jornadas.md):
/// 1. Lunes 22:00–martes 06:00: recalcular solo el martes no convierte la salida del lunes en entrada del martes.
/// 2. Calcular 30 días juntos, en orden diario y con particiones en orden distinto produce el mismo resultado.
/// 3. Un marcaje tardío reconstruye las jornadas dependientes sin alterar otras.
/// 4. Turnos que cruzan medianoche y jornadas de 24 horas no dependen de Pending heredado.
/// 5. Asignaciones inválidas fallan sin dejar una reconstrucción parcial.
/// Todo se ejecuta con el orquestador y los subcomandos reales sobre un almacén en memoria.
/// </summary>
public class Phase02PunchOwnershipTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly DateTime Tuesday = Monday.AddDays(1);
    private static readonly DateTime Wednesday = Monday.AddDays(2);

    private static readonly EmployeeId NightId = EmployeeId.From("EMP-NOC-001");
    private static readonly EmployeeId RotatingId = EmployeeId.From("EMP-ROT-002");
    private static readonly EmployeeId RegularId = EmployeeId.From("EMP-FIX-001");
    private static readonly EmployeeId GuardId = EmployeeId.From("EMP-G24-003");
    private static readonly EmployeeId SplitId = EmployeeId.From("EMP-PAR-004");

    #region Construcción de escenarios

    private static Employee CreateEmployee(EmployeeId id, ShiftType type, Shift? schedule, IEnumerable<WeekDay>? restDays = null) =>
        Employee.Create(
            id: id,
            firstName: "Empleado",
            lastName: "Sintetico",
            email: null,
            phoneNumber: null,
            hireDate: new DateTime(2024, 1, 1),
            gender: Gender.Male,
            branchId: AttendanceRegressionFixtures.BranchNorthId,
            departmentId: AttendanceRegressionFixtures.DeptOpsId,
            positionId: AttendanceRegressionFixtures.PosOperatorId,
            shiftType: type,
            scheduleId: schedule?.Id,
            restDays: restDays ?? Array.Empty<WeekDay>(),
            overtimeAuthorized: true);

    private static AttendanceRecord Punch(AttendanceRecalculationHarness h, EmployeeId employeeId, DateTime civil, AttendanceStatus? status = null)
    {
        var record = AttendanceRegressionFixtures.CreatePunch(employeeId, civil, CheckType.CheckIn, status: status);
        h.Store.Records.Add(record);
        return record;
    }

    /// <summary>Turno nocturno 22:00–06:00 con salida del lunes en el martes y jornada regular del martes.</summary>
    private static AttendanceRecalculationHarness NightThenRegularScenario()
    {
        var h = new AttendanceRecalculationHarness();
        var night = AttendanceRegressionFixtures.CreateNightShift();
        var regular = AttendanceRegressionFixtures.CreateRegularShift();
        h.Store.Shifts.AddRange(new[] { night, regular });
        h.Store.Employees.Add(CreateEmployee(RotatingId, ShiftType.Rotativo, null));
        h.Store.Rosters.Add(ShiftRoster.Create(RotatingId, Monday, night.Id, isRestDay: false));
        h.Store.Rosters.Add(ShiftRoster.Create(RotatingId, Tuesday, regular.Id, isRestDay: false));

        Punch(h, RotatingId, Monday.AddHours(22));
        Punch(h, RotatingId, Tuesday.AddHours(6));
        Punch(h, RotatingId, Tuesday.AddHours(8));
        Punch(h, RotatingId, Tuesday.AddHours(16));
        return h;
    }

    /// <summary>
    /// 30 días, cinco empleados: nocturno, rotativo semanal noche/día (roster), regular con comida,
    /// guardia de 24 h en días alternos y partido. Generación determinista (semilla fija).
    /// </summary>
    private static AttendanceRecalculationHarness ThirtyDayScenario(Func<int, AttendanceStatus>? initialStatus = null)
    {
        var h = new AttendanceRecalculationHarness();
        var night = AttendanceRegressionFixtures.CreateNightShift();
        var regular = AttendanceRegressionFixtures.CreateRegularShift();
        var guard = AttendanceRegressionFixtures.CreateContinuous24hShift();
        var split = Shift.Create(
            name: "Partido 9-13 y 15-19",
            startTime: new TimeSpan(9, 0, 0),
            toleranceMinutes: 10,
            workHours: TimeSpan.FromHours(4),
            shiftType: ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(15, 0, 0),
            secondBlockEndTime: new TimeSpan(19, 0, 0),
            secondBlockToleranceMinutes: 10);
        h.Store.Shifts.AddRange(new[] { night, regular, guard, split });

        h.Store.Employees.Add(CreateEmployee(NightId, ShiftType.Nocturno, night));
        h.Store.Employees.Add(CreateEmployee(RotatingId, ShiftType.Rotativo, null));
        h.Store.Employees.Add(CreateEmployee(RegularId, ShiftType.Matutino, regular, new[] { WeekDay.Domingo }));
        h.Store.Employees.Add(CreateEmployee(GuardId, ShiftType.Continuo, guard));
        h.Store.Employees.Add(CreateEmployee(SplitId, ShiftType.Partido, split, new[] { WeekDay.Domingo }));

        var random = new Random(20261019);
        var punchIndex = 0;
        void Add(EmployeeId id, DateTime civil) =>
            Punch(h, id, civil, initialStatus?.Invoke(punchIndex++));

        // 30 días de marcajes; la última salida nocturna cae en el día 31 y es candidata del día 30.
        for (int day = 0; day < 30; day++)
        {
            var date = Monday.AddDays(day);

            // Nocturno: sin salida cada 7 días y doble toque en el día 5.
            Add(NightId, date.AddHours(21).AddMinutes(50 + random.Next(0, 15)));
            if (day == 5) Add(NightId, date.AddHours(22).AddMinutes(10));
            if (day % 7 != 3) Add(NightId, date.AddDays(1).AddHours(6).AddMinutes(random.Next(-5, 10)));

            // Rotativo: semanas pares de noche, impares de día (dependencia hacia atrás cada lunes).
            var weekIsNight = (day / 7) % 2 == 0;
            h.Store.Rosters.Add(ShiftRoster.Create(RotatingId, date, weekIsNight ? night.Id : regular.Id, isRestDay: false));
            if (weekIsNight)
            {
                Add(RotatingId, date.AddHours(21).AddMinutes(55 + random.Next(0, 10)));
                Add(RotatingId, date.AddDays(1).AddHours(6).AddMinutes(random.Next(0, 8)));
            }
            else
            {
                Add(RotatingId, date.AddHours(7).AddMinutes(50 + random.Next(0, 15)));
                Add(RotatingId, date.AddHours(12).AddMinutes(random.Next(0, 5)));
                Add(RotatingId, date.AddHours(13).AddMinutes(random.Next(0, 5)));
                Add(RotatingId, date.AddHours(16).AddMinutes(random.Next(0, 10)));
            }

            // Regular: descansa en domingo y olvida la salida cada 9 días.
            if (date.DayOfWeek != DayOfWeek.Sunday)
            {
                Add(RegularId, date.AddHours(7).AddMinutes(55 + random.Next(0, 20)));
                Add(RegularId, date.AddHours(12).AddMinutes(random.Next(0, 10)));
                Add(RegularId, date.AddHours(13).AddMinutes(random.Next(0, 10)));
                if (day % 9 != 4) Add(RegularId, date.AddHours(16).AddMinutes(random.Next(0, 30)));
            }

            // Guardia 24 h en días alternos: salida a la mañana siguiente.
            if (day % 2 == 0)
            {
                Add(GuardId, date.AddHours(7).AddMinutes(50 + random.Next(0, 15)));
                Add(GuardId, date.AddDays(1).AddHours(1).AddMinutes(random.Next(0, 30)));
            }

            // Partido 9-13 / 15-19 con horas extra ocasionales.
            if (date.DayOfWeek != DayOfWeek.Sunday)
            {
                Add(SplitId, date.AddHours(8).AddMinutes(55 + random.Next(0, 10)));
                Add(SplitId, date.AddHours(13).AddMinutes(random.Next(0, 5)));
                Add(SplitId, date.AddHours(14).AddMinutes(55 + random.Next(0, 5)));
                Add(SplitId, date.AddHours(19).AddMinutes(day % 4 == 0 ? 40 : random.Next(0, 5)));
            }
        }

        return h;
    }

    private static readonly DateTime RangeStart = Monday;
    private static readonly DateTime RangeEnd = Monday.AddDays(29);

    private static async Task<IReadOnlyList<string>> RunAsync(Func<AttendanceRecalculationHarness> build, IEnumerable<(DateTime Start, DateTime End)> partitions)
    {
        var h = build();
        foreach (var (start, end) in partitions)
        {
            await h.RecalculateAsync(start, end);
        }
        return AttendanceResultSnapshot.Capture(h.Store);
    }

    private static void AssertSameResult(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        var diff = AttendanceResultSnapshot.Diff(actual, expected);
        diff.Should().BeEmpty("los resultados deben coincidir; diferencias (- esperado, + obtenido):\n" + string.Join("\n", diff));
    }

    private static IEnumerable<(DateTime, DateTime)> Days(IEnumerable<int> offsets) =>
        offsets.Select(o => (RangeStart.AddDays(o), RangeStart.AddDays(o)));

    #endregion

    #region Verificable 1: recalcular solo el martes no roba la salida del lunes

    [Fact]
    public async Task Criterion1_RecalculatingOnlyTuesday_KeepsMondayNightExit_WhenMondayWasComputed()
    {
        var h = NightThenRegularScenario();
        await h.RecalculateAsync(Monday, Tuesday);
        var before = AttendanceResultSnapshot.Capture(h.Store);

        await h.RecalculateAsync(Tuesday, Tuesday);

        var exit = h.Punch(RotatingId, Tuesday.AddHours(6));
        var monday = h.Daily(RotatingId, Monday)!;
        var tuesday = h.Daily(RotatingId, Tuesday)!;

        // La ventana de salida del nocturno del lunes alcanza las 08:00 del martes: es una dependencia real
        // y se reconstruye, pero con el mismo resultado.
        AssertSameResult(AttendanceResultSnapshot.Capture(h.Store), before);
        monday.CheckOutRecordId.Should().Be(exit.Id);
        tuesday.CheckInRecordId.Should().Be(h.Punch(RotatingId, Tuesday.AddHours(8)).Id, "la entrada del martes es la de las 08:00, no la salida del lunes");
        tuesday.CheckOutRecordId.Should().Be(h.Punch(RotatingId, Tuesday.AddHours(16)).Id);
        tuesday.LateMinutes.Should().Be(0);

        var assignment = h.Store.Assignments.Single(a => a.AttendanceRecordId == exit.Id);
        assignment.WorkDate.Should().Be(DateOnly.FromDateTime(Monday));
        assignment.Role.Should().Be(PunchAssignmentRole.CheckOut);
        exit.Status.Should().Be(AttendanceStatus.Processed);
    }

    [Fact]
    public async Task Criterion1_RecalculatingOnlyTuesday_IncludesMondayDependency_WhenMondayWasNeverComputed()
    {
        var h = NightThenRegularScenario();

        await h.RecalculateAsync(Tuesday, Tuesday);

        var monday = h.Daily(RotatingId, Monday);
        monday.Should().NotBeNull("el turno real del lunes alcanza un marcaje libre del martes, así que forma parte de la unidad");
        monday!.CheckOutRecordId.Should().Be(h.Punch(RotatingId, Tuesday.AddHours(6)).Id);
        h.Daily(RotatingId, Tuesday)!.CheckInRecordId.Should().Be(h.Punch(RotatingId, Tuesday.AddHours(8)).Id);
    }

    [Fact]
    public async Task Criterion1_LegacyCorruptState_IsRepairedByChronologicalPrecedence()
    {
        // Estado heredado de H02: la salida del lunes quedó como entrada del martes y sin filas de asignación.
        var h = NightThenRegularScenario();
        await h.RecalculateAsync(Monday, Tuesday);
        var expected = AttendanceResultSnapshot.Capture(h.Store);

        var exit = h.Punch(RotatingId, Tuesday.AddHours(6));
        var corruptTuesday = DailyAttendance.Create(RotatingId, Tuesday, AttendanceRegressionFixtures.CreateRegularShift(),
            exit.CheckTime, null, false, exit.Id, null);
        h.Store.Dailies.Remove(h.Daily(RotatingId, Tuesday)!);
        h.Store.Dailies.Add(corruptTuesday);
        h.Store.Assignments.Clear();

        await h.RecalculateAsync(Tuesday, Tuesday);

        h.Daily(RotatingId, Tuesday)!.CheckInRecordId.Should().Be(h.Punch(RotatingId, Tuesday.AddHours(8)).Id);
        h.Daily(RotatingId, Monday)!.CheckOutRecordId.Should().Be(exit.Id);
        AssertSameResult(AttendanceResultSnapshot.Capture(h.Store), expected);
    }

    #endregion

    #region Verificable 2: completo, diario y particionado producen el mismo resultado

    [Fact]
    public async Task Criterion2_FullDailyAndShuffledPartitions_ProduceIdenticalResults()
    {
        var full = await RunAsync(() => ThirtyDayScenario(), new[] { (RangeStart, RangeEnd) });
        full.Should().Contain(l => l.StartsWith("DA|")).And.Contain(l => l.StartsWith("PA|"));

        var dailyAscending = await RunAsync(() => ThirtyDayScenario(), Days(Enumerable.Range(0, 30)));
        var dailyDescending = await RunAsync(() => ThirtyDayScenario(), Days(Enumerable.Range(0, 30).Reverse()));
        var shuffledChunks = await RunAsync(() => ThirtyDayScenario(), new[]
        {
            (RangeStart.AddDays(20), RangeStart.AddDays(29)),
            (RangeStart.AddDays(0), RangeStart.AddDays(6)),
            (RangeStart.AddDays(14), RangeStart.AddDays(19)),
            (RangeStart.AddDays(7), RangeStart.AddDays(13))
        });
        var interleaved = await RunAsync(() => ThirtyDayScenario(),
            Days(Enumerable.Range(0, 30).Where(d => d % 2 == 1)).Concat(Days(Enumerable.Range(0, 30).Where(d => d % 2 == 0).Reverse())));

        AssertSameResult(dailyAscending, full);
        AssertSameResult(dailyDescending, full);
        AssertSameResult(shuffledChunks, full);
        AssertSameResult(interleaved, full);
    }

    [Fact]
    public async Task Criterion2_RepeatedRecalculation_IsIdempotent()
    {
        var h = ThirtyDayScenario();
        await h.RecalculateAsync(RangeStart, RangeEnd);
        var first = AttendanceResultSnapshot.Capture(h.Store);

        await h.RecalculateAsync(RangeStart, RangeEnd);
        await h.RecalculateAsync(RangeStart.AddDays(10), RangeStart.AddDays(12));

        AssertSameResult(AttendanceResultSnapshot.Capture(h.Store), first);
    }

    [Fact]
    public async Task Criterion2_NoPunchIsAssignedToMoreThanOneJornadaOrPosition()
    {
        var h = ThirtyDayScenario();
        await h.RecalculateAsync(RangeStart, RangeEnd);

        var references = h.Store.Dailies.SelectMany(d => new[] { d.CheckInRecordId, d.Block1CheckOutRecordId, d.Block2CheckInRecordId, d.CheckOutRecordId })
            .Where(id => id != null)
            .ToList();
        references.Should().OnlyHaveUniqueItems("C-04: un marcaje ocupa una sola posición en una sola jornada");

        // Cada posición de DailyAttendance tiene su fila de asignación con la misma jornada.
        foreach (var daily in h.Store.Dailies)
        {
            foreach (var (id, role) in new[]
                     {
                         (daily.CheckInRecordId, PunchAssignmentRole.CheckIn),
                         (daily.Block1CheckOutRecordId, PunchAssignmentRole.Block1CheckOut),
                         (daily.Block2CheckInRecordId, PunchAssignmentRole.Block2CheckIn),
                         (daily.CheckOutRecordId, PunchAssignmentRole.CheckOut)
                     })
            {
                if (id == null) continue;
                var row = h.Store.Assignments.Single(a => a.AttendanceRecordId == id);
                row.WorkDate.Should().Be(DateOnly.FromDateTime(daily.Date));
                row.Role.Should().Be(role);
                row.EmployeeId.Should().Be(daily.EmployeeId);
            }
        }

        // Estado derivado: Processed si y solo si el marcaje tiene asignación.
        var assigned = h.Store.Assignments.Select(a => a.AttendanceRecordId).ToHashSet();
        foreach (var record in h.Store.Records.Where(r => r.WorkDate <= DateOnly.FromDateTime(RangeEnd)))
        {
            (record.Status == AttendanceStatus.Processed).Should().Be(assigned.Contains(record.Id),
                $"el estado de {record.EmployeeId.Value} {record.OriginalCheckTime:yyyy-MM-dd HH:mm} debe derivarse de su asignación");
        }
    }

    #endregion

    #region Verificable 3: un marcaje tardío reconstruye solo las jornadas dependientes

    [Fact]
    public async Task Criterion3_LatePunch_RebuildsDependentJornadas_WithoutAlteringOthers()
    {
        var h = ThirtyDayScenario();
        await h.RecalculateAsync(RangeStart, RangeEnd);

        // Día 3 (jueves) del nocturno no tenía salida; llega tarde la del viernes 06:02.
        var thursday = Monday.AddDays(3);
        var friday = Monday.AddDays(4);
        h.Daily(NightId, thursday)!.CheckOutRecordId.Should().BeNull("precondición: salida faltante");

        var untouched = h.Store.Dailies.Where(d => d.EmployeeId != NightId || (d.Date != thursday && d.Date != friday)).ToList();
        var late = Punch(h, NightId, friday.AddHours(6).AddMinutes(2));

        // La consolidación solicita la fecha laboral del marcaje.
        await h.RecalculateAsync(friday, friday, NightId);

        h.Daily(NightId, thursday)!.CheckOutRecordId.Should().Be(late.Id, "el marcaje tardío pertenece a la jornada nocturna del jueves");
        h.Daily(NightId, friday)!.CheckInRecordId.Should().NotBe(late.Id);
        foreach (var other in untouched)
        {
            h.Store.Dailies.Should().Contain(d => ReferenceEquals(d, other), "las jornadas independientes no se reconstruyen");
        }

        // El resultado coincide con recalcular todo desde cero con el marcaje ya presente.
        var reference = ThirtyDayScenario();
        Punch(reference, NightId, friday.AddHours(6).AddMinutes(2));
        await reference.RecalculateAsync(RangeStart, RangeEnd);
        AssertSameResult(AttendanceResultSnapshot.Capture(h.Store), AttendanceResultSnapshot.Capture(reference.Store));
    }

    [Fact]
    public async Task Criterion3_ForwardDependency_ExtendsUnitWhenEarlierJornadaTakesAPunchFromALaterOne()
    {
        // Estado previo: el martes (regular) tomó la salida del lunes como entrada y el lunes no tiene salida.
        var h = NightThenRegularScenario();
        await h.RecalculateAsync(Monday, Tuesday);
        var expected = AttendanceResultSnapshot.Capture(h.Store);

        var mondayIn = h.Punch(RotatingId, Monday.AddHours(22));
        var exit = h.Punch(RotatingId, Tuesday.AddHours(6));
        var tuesdayOut = h.Punch(RotatingId, Tuesday.AddHours(16));
        var regular = h.Store.Shifts.Single(s => s.ShiftType == ShiftType.Matutino);
        var night = h.Store.Shifts.Single(s => s.ShiftType == ShiftType.Nocturno);

        h.Store.Dailies.Clear();
        h.Store.Dailies.Add(DailyAttendance.Create(RotatingId, Monday, night, mondayIn.CheckTime, null, false, mondayIn.Id, null));
        h.Store.Dailies.Add(DailyAttendance.Create(RotatingId, Tuesday, regular, exit.CheckTime, tuesdayOut.CheckTime, false, exit.Id, tuesdayOut.Id));
        var tuesdayBefore = h.Daily(RotatingId, Tuesday)!;
        h.Store.Assignments.Clear();
        h.Store.Assignments.Add(PunchAssignment.Create(mondayIn.Id, RotatingId, DateOnly.FromDateTime(Monday), PunchAssignmentRole.CheckIn, PunchAssignmentOrigin.Automatic));
        h.Store.Assignments.Add(PunchAssignment.Create(exit.Id, RotatingId, DateOnly.FromDateTime(Tuesday), PunchAssignmentRole.CheckIn, PunchAssignmentOrigin.Automatic));
        h.Store.Assignments.Add(PunchAssignment.Create(tuesdayOut.Id, RotatingId, DateOnly.FromDateTime(Tuesday), PunchAssignmentRole.CheckOut, PunchAssignmentOrigin.Automatic));
        h.Punch(RotatingId, Tuesday.AddHours(8)).ResetStatus();

        // Recalcular solo el lunes: por precedencia cronológica toma su salida y la unidad se extiende al martes.
        await h.RecalculateAsync(Monday, Monday);

        h.Daily(RotatingId, Monday)!.CheckOutRecordId.Should().Be(exit.Id);
        h.Daily(RotatingId, Tuesday).Should().NotBeSameAs(tuesdayBefore, "el martes dependía del lunes y se reconstruyó");
        h.Daily(RotatingId, Tuesday)!.CheckInRecordId.Should().Be(h.Punch(RotatingId, Tuesday.AddHours(8)).Id);
        h.Store.Assignments.Single(a => a.AttendanceRecordId == exit.Id).WorkDate.Should().Be(DateOnly.FromDateTime(Monday));
        AssertSameResult(AttendanceResultSnapshot.Capture(h.Store), expected);
    }

    #endregion

    #region Verificable 4: nocturnos y 24 h no dependen del Pending heredado

    [Fact]
    public async Task Criterion4_CrossMidnightAnd24hJornadas_IgnoreInheritedStatus()
    {
        var fromPending = await RunAsync(() => ThirtyDayScenario(_ => AttendanceStatus.Pending), new[] { (RangeStart, RangeEnd) });
        var fromProcessed = await RunAsync(() => ThirtyDayScenario(_ => AttendanceStatus.Processed), new[] { (RangeStart, RangeEnd) });
        var fromMixed = await RunAsync(() => ThirtyDayScenario(i => i % 3 == 0 ? AttendanceStatus.Processed : AttendanceStatus.Pending), new[] { (RangeStart, RangeEnd) });
        var partitionedFromProcessed = await RunAsync(() => ThirtyDayScenario(_ => AttendanceStatus.Processed), Days(Enumerable.Range(0, 30).Reverse()));

        AssertSameResult(fromProcessed, fromPending);
        AssertSameResult(fromMixed, fromPending);
        AssertSameResult(partitionedFromProcessed, fromPending);

        // Comprobación explícita de una jornada nocturna y una de guardia.
        var h = ThirtyDayScenario(_ => AttendanceStatus.Processed);
        await h.RecalculateAsync(RangeStart, RangeEnd);
        h.Daily(NightId, Monday)!.CheckOutRecordId.Should().NotBeNull();
        h.Daily(GuardId, Monday)!.CheckOutRecordId.Should().NotBeNull("la salida de la guardia está en la madrugada siguiente");
        h.Daily(GuardId, Tuesday)!.CheckInRecordId.Should().BeNull("la salida de la guardia del lunes no se reutiliza como entrada del martes");
    }

    #endregion

    #region Verificable 5: asignaciones inválidas fallan sin reconstrucción parcial

    [Fact]
    public async Task Criterion5_ManualAdjustmentReferencingForeignPunch_FailsAndRollsBackEverything()
    {
        var h = ThirtyDayScenario();
        await h.RecalculateAsync(RangeStart, RangeEnd);
        var before = AttendanceResultSnapshot.Capture(h.Store);
        var dailiesBefore = h.Store.Dailies.ToList();

        // Ajuste heredado (sin validación previa) que asigna al regular un marcaje del nocturno.
        var foreign = h.Store.Records.First(r => r.EmployeeId == NightId && r.WorkDate == DateOnly.FromDateTime(Tuesday));
        var adjustment = AttendanceAdjustment.Create(RegularId, Tuesday, "Legado");
        adjustment.SetCheckIn(foreign.Id, foreign.CheckTime, "Legado");
        h.Store.Adjustments.Add(adjustment);

        var act = () => h.RecalculateAsync(Monday, Wednesday);

        // F03-04: la unidad del empleado afectado se revierte; las demás se confirman y el fallo se informa.
        var failure = (await act.Should().ThrowAsync<AttendanceRecalculationException>()).Which;
        failure.FailedUnits.Should().ContainSingle(f => f.EmployeeId == RegularId.Value && f.ErrorType == nameof(PunchAssignmentInvariantException));
        failure.SuccessfulUnits.Should().Be(4);
        failure.InnerException.Should().BeOfType<PunchAssignmentInvariantException>()
            .Which.Violations.Should().Contain(v => v.Contains("pertenece a EMP-NOC-001"));
        h.Store.Dailies.Where(d => d.EmployeeId == RegularId).Should().Equal(dailiesBefore.Where(d => d.EmployeeId == RegularId),
            "el rollback conserva las jornadas previas del empleado, sin reemplazos parciales");
        AssertSameResult(AttendanceResultSnapshot.Capture(h.Store), before);
    }

    [Fact]
    public async Task Criterion5_SamePunchManuallyAssignedToTwoJornadas_FailsAndRollsBackEverything()
    {
        var h = NightThenRegularScenario();
        await h.RecalculateAsync(Monday, Tuesday);
        var before = AttendanceResultSnapshot.Capture(h.Store);

        var exit = h.Punch(RotatingId, Tuesday.AddHours(6));
        var monday = AttendanceAdjustment.Create(RotatingId, Monday, "Legado");
        monday.SetCheckOut(exit.Id, exit.CheckTime, "Legado");
        var tuesday = AttendanceAdjustment.Create(RotatingId, Tuesday, "Legado");
        tuesday.SetCheckIn(exit.Id, exit.CheckTime, "Legado");
        h.Store.Adjustments.AddRange(new[] { monday, tuesday });

        var act = () => h.RecalculateAsync(Monday, Tuesday);

        (await act.Should().ThrowAsync<PunchAssignmentInvariantException>())
            .Which.Violations.Should().Contain(v => v.Contains("no puede reutilizarse"));
        AssertSameResult(AttendanceResultSnapshot.Capture(h.Store), before);
    }

    [Fact]
    public async Task ManualAssignment_IsPreservedAndReservedAgainstAutomaticSelection()
    {
        var h = NightThenRegularScenario();
        // RH decide que la entrada del martes es la de las 06:00 (llegó antes) y la salida del lunes queda vacía.
        var exit = h.Punch(RotatingId, Tuesday.AddHours(6));
        var adjustment = AttendanceAdjustment.Create(RotatingId, Tuesday, "Usuario RH");
        adjustment.SetCheckIn(exit.Id, exit.CheckTime, "Usuario RH", "Entrada anticipada autorizada");
        h.Store.Adjustments.Add(adjustment);

        await h.RecalculateAsync(Monday, Tuesday);

        h.Daily(RotatingId, Tuesday)!.CheckInRecordId.Should().Be(exit.Id);
        h.Daily(RotatingId, Monday)!.CheckOutRecordId.Should().NotBe(exit.Id, "la decisión manual reserva el marcaje frente a la selección automática");
        var row = h.Store.Assignments.Single(a => a.AttendanceRecordId == exit.Id);
        row.Origin.Should().Be(PunchAssignmentOrigin.Manual);
        row.WorkDate.Should().Be(DateOnly.FromDateTime(Tuesday));
    }

    #endregion
}
