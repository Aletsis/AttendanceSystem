using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.RegisterManualAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.SetDailyRestDayOverride;
using AttendanceSystem.Application.Features.Attendance.Decisions;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase05;

/// <summary>
/// Decisiones manuales persistentes y seguras, con el orquestador y los procesadores reales:
/// 1. Toda decisión permanece después de dos recálculos y de procesar días vecinos.
/// 2. MultiInterval aplica descanso, autorizaciones y asignaciones.
/// 3. Vacío forzado impide la reasignación automática; retirar el override la permite.
/// 4. Marcaje ajeno u ocupado se rechaza sin cambios parciales.
/// 5. Mover entrada a salida no recrea ambas posiciones.
/// 6. Turno mixto sin configuración diaria respeta la precedencia manual.
/// 7. Dos operadores sobre la misma revisión obtienen resultado determinista o conflicto visible.
/// </summary>
public class Phase05ManualDecisionTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly DateTime Tuesday = Monday.AddDays(1);
    private static readonly DateTime Wednesday = Monday.AddDays(2);
    private static readonly DateTime Saturday = Monday.AddDays(5);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-DEC-001");
    private static readonly EmployeeId Other = EmployeeId.From("EMP-DEC-002");

    #region Escenarios

    private static Employee CreateEmployee(EmployeeId id, Shift? schedule) =>
        Employee.Create(id, "Empleado", "Sintetico", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            schedule?.ShiftType ?? ShiftType.Rotativo, schedule?.Id, restDays: Array.Empty<WeekDay>(), overtimeAuthorized: true);

    private static AttendanceRecord Punch(AttendanceRecalculationHarness h, EmployeeId id, DateTime civil)
    {
        var record = AttendanceRegressionFixtures.CreatePunch(id, civil, CheckType.CheckIn);
        h.Store.Records.Add(record);
        return record;
    }

    private static Shift Regular() => AttendanceRegressionFixtures.CreateRegularShift();

    /// <summary>Turno regular 08–16 de lunes a miércoles con una entrada tardía extra el martes.</summary>
    private static AttendanceRecalculationHarness RegularWeek(out Shift shift)
    {
        var h = new AttendanceRecalculationHarness();
        shift = Regular();
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(CreateEmployee(Emp, shift));
        foreach (var day in new[] { Monday, Tuesday, Wednesday })
        {
            Punch(h, Emp, day.AddHours(7).AddMinutes(55));
            Punch(h, Emp, day.AddHours(16).AddMinutes(5));
        }
        Punch(h, Emp, Tuesday.AddHours(8).AddMinutes(20));
        return h;
    }

    private static Task<Result<AttendanceDecisionOutcome>> Assign(AttendanceRecalculationHarness h, AttendanceRecord record, DateTime day, string type, int? revision = null, bool move = false) =>
        h.ManualAssign().Handle(new ManuallyAssignAttendanceCommand(record.EmployeeId.Value, DateOnly.FromDateTime(day), record.Id.Value.ToString(), type, revision, move, "Prueba"), CancellationToken.None);

    private static async Task RecalculateAroundAsync(AttendanceRecalculationHarness h, DateTime day)
    {
        await h.RecalculateAsync(day, day);
        await h.RecalculateAsync(day, day);
        await h.RecalculateAsync(day.AddDays(-1), day.AddDays(-1));
        await h.RecalculateAsync(day.AddDays(1), day.AddDays(1));
        await h.RecalculateAsync(day.AddDays(-1), day.AddDays(1));
    }

    #endregion

    #region 1: persistencia frente a recálculos y días vecinos

    [Fact]
    public async Task Criterion1_EveryDecisionType_SurvivesTwoRecalculationsAndNeighbourDays()
    {
        var h = RegularWeek(out _);
        await h.RecalculateAsync(Monday, Wednesday);
        var late = h.Punch(Emp, Tuesday.AddHours(8).AddMinutes(20));
        var decisions = h.Decisions();
        var tuesday = DateOnly.FromDateTime(Tuesday);

        (await Assign(h, late, Tuesday, "Entrada")).IsSuccess.Should().BeTrue();
        (await new ClassifyTemporaryExitCommandHandler(decisions).Handle(new ClassifyTemporaryExitCommand(Emp.Value, tuesday, TemporaryExitStatus.ApprovedPaid, "Cita médica"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await new SetDailyOvertimeAuthorizationCommandHandler(decisions).Handle(new SetDailyOvertimeAuthorizationCommand(Emp.Value, tuesday, false, false, "Sin autorización"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        var daily = h.Daily(Emp, Tuesday)!;
        (await new SetDailyRestDayOverrideCommandHandler(h.DailyRepository, decisions).Handle(new SetDailyRestDayOverrideCommand(daily.Id.Value, true), CancellationToken.None)).Should().BeTrue();

        await RecalculateAroundAsync(h, Tuesday);

        var result = h.Daily(Emp, Tuesday)!;
        result.CheckInRecordId.Should().Be(late.Id, "la entrada asignada por RH prevalece sobre la de las 07:55");
        result.TemporaryExitStatus.Should().Be(TemporaryExitStatus.ApprovedPaid);
        result.OvertimeAuthorized.Should().BeFalse();
        result.IsRestDay.Should().BeTrue();
        h.Daily(Emp, Monday)!.CheckInRecordId.Should().Be(h.Punch(Emp, Monday.AddHours(7).AddMinutes(55)).Id, "los días vecinos no se alteran");

        var adjustment = h.Store.Adjustments.Single();
        adjustment.Revision.Should().Be(4);
        h.Store.AdjustmentHistory.Select(e => e.Action).Should().Equal("Entrada", "SalidaTemporal", "HorasExtras", "Descanso");
        h.Store.AdjustmentHistory.Should().OnlyContain(e => e.ChangedBy == h.CurrentUser, "F05-06: el autor es el usuario autenticado");
        h.Store.AdjustmentHistory[0].Before.Should().Contain("CheckIn=auto");
        h.Store.AdjustmentHistory[0].After.Should().Contain($"CheckIn=asignado:{late.Id}");
        h.Store.ConsolidationRequests.Should().HaveCount(4, "F05-07: cada decisión deja una consolidación durable");
    }

    [Fact]
    public async Task Criterion1_DecisionIsSavedEvenIfImmediateRecalculationFails_AndReportedAsPending()
    {
        var h = RegularWeek(out _);
        await h.RecalculateAsync(Monday, Wednesday);
        var late = h.Punch(Emp, Tuesday.AddHours(8).AddMinutes(20));
        h.FailNextRecalculation = true;

        var result = await Assign(h, late, Tuesday, "Entrada");

        result.IsSuccess.Should().BeTrue();
        result.Value.Consolidated.Should().BeFalse();
        result.Value.UserMessage.Should().Contain("pendiente de consolidación");
        h.Store.ConsolidationRequests.Should().ContainSingle(r => r.Start == Tuesday && r.End == Tuesday);

        // El consumidor (aquí, un recálculo posterior) aplica la decisión durable.
        await h.RecalculateAsync(Tuesday, Tuesday);
        h.Daily(Emp, Tuesday)!.CheckInRecordId.Should().Be(late.Id);
    }

    #endregion

    #region 2: MultiInterval

    [Fact]
    public async Task Criterion2_MultiInterval_AppliesRestDayAuthorizationAndAssignments()
    {
        var h = new AttendanceRecalculationHarness();
        var multi = AttendanceRegressionFixtures.CreateMultiIntervalShift();
        h.Store.Shifts.Add(multi);
        h.Store.Employees.Add(CreateEmployee(Emp, multi));
        Punch(h, Emp, Tuesday.AddHours(8));
        Punch(h, Emp, Tuesday.AddHours(12));
        var back = Punch(h, Emp, Tuesday.AddHours(13));
        Punch(h, Emp, Tuesday.AddHours(17));
        await h.RecalculateAsync(Tuesday, Tuesday);

        var decisions = h.Decisions();
        var day = DateOnly.FromDateTime(Tuesday);
        await new SetDailyOvertimeAuthorizationCommandHandler(decisions).Handle(new SetDailyOvertimeAuthorizationCommand(Emp.Value, day, false, true), CancellationToken.None);
        await new SetDailyRestDayOverrideCommandHandler(h.DailyRepository, decisions).Handle(new SetDailyRestDayOverrideCommand(h.Daily(Emp, Tuesday)!.Id.Value, true), CancellationToken.None);
        (await Assign(h, back, Tuesday, "Entrada")).IsSuccess.Should().BeTrue();

        await RecalculateAroundAsync(h, Tuesday);

        var daily = h.Daily(Emp, Tuesday)!;
        daily.IsRestDay.Should().BeTrue();
        daily.OvertimeAuthorized.Should().BeFalse();
        daily.CalculateOvertimeBeforeEntry.Should().BeTrue();
        daily.CheckInRecordId.Should().Be(back.Id);
    }

    #endregion

    #region 3: vacío forzado frente a retirar la decisión

    [Fact]
    public async Task Criterion3_ForcedEmpty_PreventsAutomaticReassignment_AndRestoreAllowsIt()
    {
        var h = RegularWeek(out _);
        await h.RecalculateAsync(Monday, Wednesday);
        var exit = h.Punch(Emp, Tuesday.AddHours(16).AddMinutes(5));
        h.Daily(Emp, Tuesday)!.CheckOutRecordId.Should().Be(exit.Id, "precondición: salida automática");

        (await Assign(h, exit, Tuesday, ManuallyAssignAttendanceCommandHandler.Unassign)).IsSuccess.Should().BeTrue();
        await RecalculateAroundAsync(h, Tuesday);

        var daily = h.Daily(Emp, Tuesday)!;
        daily.CheckOutRecordId.Should().BeNull("el vacío forzado se mantiene tras recalcular");
        daily.MissingCheckOut.Should().BeTrue();
        h.Store.Assignments.Single(a => a.AttendanceRecordId == exit.Id).Role.Should().Be(PunchAssignmentRole.ManuallyExcluded);
        h.Store.Assignments.Should().NotContain(a => a.AttendanceRecordId == exit.Id && a.Role == PunchAssignmentRole.CheckOut);

        (await Assign(h, exit, Tuesday, ManuallyAssignAttendanceCommandHandler.Restore)).IsSuccess.Should().BeTrue();
        await h.RecalculateAsync(Tuesday, Tuesday);

        h.Daily(Emp, Tuesday)!.CheckOutRecordId.Should().Be(exit.Id, "retirar el override devuelve la decisión al cálculo automático");
    }

    [Fact]
    public async Task Criterion3_ForcedEmpty_ExcludedPunchIsNotTakenByANeighbourJornada()
    {
        // Lunes nocturno y martes regular: RH retira la salida de las 06:00 del lunes (no era una salida real).
        var h = new AttendanceRecalculationHarness();
        var night = AttendanceRegressionFixtures.CreateNightShift();
        var regular = Regular();
        h.Store.Shifts.AddRange(new[] { night, regular });
        h.Store.Employees.Add(CreateEmployee(Emp, null));
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Monday, night.Id, isRestDay: false));
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Tuesday, regular.Id, isRestDay: false));
        Punch(h, Emp, Monday.AddHours(22));
        var stray = Punch(h, Emp, Tuesday.AddHours(6));
        var tuesdayIn = Punch(h, Emp, Tuesday.AddHours(8));
        Punch(h, Emp, Tuesday.AddHours(16));
        await h.RecalculateAsync(Monday, Tuesday);
        h.Daily(Emp, Monday)!.CheckOutRecordId.Should().Be(stray.Id, "precondición");

        (await Assign(h, stray, Monday, ManuallyAssignAttendanceCommandHandler.Unassign)).IsSuccess.Should().BeTrue();
        await h.RecalculateAsync(Tuesday, Tuesday);
        await h.RecalculateAsync(Monday, Tuesday);

        h.Daily(Emp, Monday)!.CheckOutRecordId.Should().BeNull();
        h.Daily(Emp, Tuesday)!.CheckInRecordId.Should().Be(tuesdayIn.Id, "el marcaje excluido no se reasigna en ninguna jornada");
        h.Store.Assignments.Single(a => a.AttendanceRecordId == stray.Id).WorkDate.Should().Be(DateOnly.FromDateTime(Monday));
    }

    #endregion

    #region 4: rechazos sin cambios parciales

    [Fact]
    public async Task Criterion4_ForeignOrOccupiedPunch_IsRejectedWithoutPartialChanges()
    {
        var h = RegularWeek(out var shift);
        h.Store.Employees.Add(CreateEmployee(Other, shift));
        var foreign = Punch(h, Other, Tuesday.AddHours(8));
        await h.RecalculateAsync(Monday, Wednesday);
        var mondayExit = h.Punch(Emp, Monday.AddHours(16).AddMinutes(5));
        var saves = h.Store.SaveChangesCount;
        var snapshot = AttendanceResultSnapshot.Capture(h.Store);

        var foreignResult = await h.ManualAssign().Handle(new ManuallyAssignAttendanceCommand(Emp.Value, DateOnly.FromDateTime(Tuesday), foreign.Id.Value.ToString(), "Entrada"), CancellationToken.None);
        var occupiedResult = await Assign(h, mondayExit, Tuesday, "Salida");

        foreignResult.Error.Should().Be("El marcaje no pertenece al empleado indicado.");
        occupiedResult.IsSuccess.Should().BeFalse();
        occupiedResult.Error.Should().Contain("03/08/2026");
        h.Store.SaveChangesCount.Should().Be(saves);
        h.Store.Adjustments.Should().BeEmpty();
        h.Store.AdjustmentHistory.Should().BeEmpty();
        h.Store.ConsolidationRequests.Should().BeEmpty();
        AttendanceResultSnapshot.Diff(AttendanceResultSnapshot.Capture(h.Store), snapshot).Should().BeEmpty();
    }

    [Fact]
    public async Task Criterion4_ExplicitMove_ReassignsAtomically_AndRecalculatesBothJornadas()
    {
        // Legado H02: el martes (regular) tomó como entrada la salida nocturna del lunes.
        var h = new AttendanceRecalculationHarness();
        var night = AttendanceRegressionFixtures.CreateNightShift();
        var regular = Regular();
        h.Store.Shifts.AddRange(new[] { night, regular });
        h.Store.Employees.Add(CreateEmployee(Emp, null));
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Monday, night.Id, isRestDay: false));
        h.Store.Rosters.Add(ShiftRoster.Create(Emp, Tuesday, regular.Id, isRestDay: false));
        Punch(h, Emp, Monday.AddHours(22));
        var exit = Punch(h, Emp, Tuesday.AddHours(6));
        var tuesdayIn = Punch(h, Emp, Tuesday.AddHours(8));
        Punch(h, Emp, Tuesday.AddHours(16));
        // RH decidió que las 06:00 eran la entrada del martes.
        var legacy = AttendanceAdjustment.Create(Emp, Tuesday, "Legado");
        legacy.SetCheckIn(exit.Id, exit.CheckTime, "Legado");
        h.Store.Adjustments.Add(legacy);
        await h.RecalculateAsync(Monday, Tuesday);
        h.Daily(Emp, Tuesday)!.CheckInRecordId.Should().Be(exit.Id);

        (await Assign(h, exit, Monday, "Salida")).IsSuccess.Should().BeFalse("sin solicitud explícita no se mueve un marcaje ocupado");

        var moved = await Assign(h, exit, Monday, "Salida", move: true);

        moved.IsSuccess.Should().BeTrue(moved.IsSuccess ? string.Empty : moved.Error);
        moved.Value.Consolidated.Should().BeTrue(moved.Value.PendingReason);
        h.Daily(Emp, Monday)!.CheckOutRecordId.Should().Be(exit.Id);
        h.Daily(Emp, Tuesday)!.CheckInRecordId.Should().Be(tuesdayIn.Id, "el martes vuelve a decidir su entrada automáticamente");
        h.Store.Adjustments.Single(a => a.Date == Tuesday).CheckInOverride.Should().Be(PositionOverrideKind.None);
        h.Store.ConsolidationRequests.Last().Should().Be((Emp, Monday, Tuesday));
    }

    #endregion

    #region 5: mover entre posiciones

    [Fact]
    public async Task Criterion5_MovingCheckInToCheckOut_DoesNotRecreateBothPositions()
    {
        var h = new AttendanceRecalculationHarness();
        var shift = Regular();
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(CreateEmployee(Emp, shift));
        var only = Punch(h, Emp, Tuesday.AddHours(9).AddMinutes(50));
        await h.RecalculateAsync(Tuesday, Tuesday);
        h.Daily(Emp, Tuesday)!.CheckInRecordId.Should().Be(only.Id, "precondición: el único marcaje se tomó como entrada");

        (await Assign(h, only, Tuesday, "Salida")).IsSuccess.Should().BeTrue();
        await RecalculateAroundAsync(h, Tuesday);

        var daily = h.Daily(Emp, Tuesday)!;
        daily.CheckOutRecordId.Should().Be(only.Id);
        daily.CheckInRecordId.Should().BeNull("el marcaje movido no vuelve a ocupar la entrada");
        h.Store.Assignments.Where(a => a.AttendanceRecordId == only.Id).Should().ContainSingle()
            .Which.Role.Should().Be(PunchAssignmentRole.CheckOut);
    }

    [Fact]
    public async Task Criterion5_AssignmentBreakingTheSequence_IsRejected()
    {
        var h = RegularWeek(out _);
        await h.RecalculateAsync(Tuesday, Tuesday);
        var exit = h.Punch(Emp, Tuesday.AddHours(16).AddMinutes(5));
        var late = h.Punch(Emp, Tuesday.AddHours(8).AddMinutes(20));
        (await Assign(h, exit, Tuesday, "Salida")).IsSuccess.Should().BeTrue();

        // Una salida anterior a la entrada vigente rompe la secuencia.
        var early = Punch(h, Emp, Tuesday.AddHours(7));
        var result = await Assign(h, early, Tuesday, "Salida B1");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("secuencia");
        _ = late;
    }

    #endregion

    #region 6: turno mixto sin configuración diaria

    [Fact]
    public async Task Criterion6_MixedShiftWithoutDayConfig_RespectsManualPrecedence()
    {
        var h = new AttendanceRecalculationHarness();
        var mixed = AttendanceRegressionFixtures.CreateMixedShift();
        h.Store.Shifts.Add(mixed);
        h.Store.Employees.Add(CreateEmployee(Emp, mixed));
        var first = Punch(h, Emp, Saturday.AddHours(9));
        var second = Punch(h, Emp, Saturday.AddHours(10));
        var last = Punch(h, Emp, Saturday.AddHours(14));
        await h.RecalculateAsync(Saturday, Saturday);
        h.Daily(Emp, Saturday)!.IsRestDay.Should().BeTrue("sábado sin configuración: descanso por defecto");

        var decisions = h.Decisions();
        await new SetDailyRestDayOverrideCommandHandler(h.DailyRepository, decisions).Handle(new SetDailyRestDayOverrideCommand(h.Daily(Emp, Saturday)!.Id.Value, false), CancellationToken.None);
        (await Assign(h, second, Saturday, "Entrada")).IsSuccess.Should().BeTrue();
        await RecalculateAroundAsync(h, Saturday);

        var daily = h.Daily(Emp, Saturday)!;
        daily.IsRestDay.Should().BeFalse("la decisión manual prevalece sobre el descanso por falta de configuración");
        daily.CheckInRecordId.Should().Be(second.Id);
        daily.CheckOutRecordId.Should().Be(last.Id);
        _ = first;
    }

    #endregion

    #region 7: dos operadores sobre la misma revisión

    [Fact]
    public async Task Criterion7_TwoOperatorsOnTheSameRevision_SecondGetsAVisibleConflict()
    {
        var h = RegularWeek(out _);
        await h.RecalculateAsync(Tuesday, Tuesday);
        var early = h.Punch(Emp, Tuesday.AddHours(7).AddMinutes(55));
        var late = h.Punch(Emp, Tuesday.AddHours(8).AddMinutes(20));
        var seenRevision = 0; // ambos operadores abrieron la jornada sin decisiones

        h.CurrentUser = "operador.a";
        (await Assign(h, late, Tuesday, "Entrada", seenRevision)).IsSuccess.Should().BeTrue();
        h.CurrentUser = "operador.b";
        var second = await Assign(h, early, Tuesday, "Entrada", seenRevision);

        second.IsSuccess.Should().BeFalse();
        second.Error.Should().Contain("revisión 1").And.Contain("se esperaba 0");
        h.Daily(Emp, Tuesday)!.CheckInRecordId.Should().Be(late.Id);
        h.Store.AdjustmentHistory.Should().ContainSingle(e => e.ChangedBy == "operador.a");

        // Sin revisión esperada la resolución es determinista: gana la última decisión serializada por el bloqueo.
        (await Assign(h, early, Tuesday, "Entrada")).IsSuccess.Should().BeTrue();
        h.Daily(Emp, Tuesday)!.CheckInRecordId.Should().Be(early.Id);
    }

    #endregion

    #region Matriz por procesador (F05-04)

    public static IEnumerable<object[]> Processors() => new[]
    {
        new object[] { "Regular", 8 },
        new object[] { "Nocturno", 22 },
        new object[] { "Flexible", 8 },
        new object[] { "MultiInterval", 8 },
        new object[] { "Partido", 9 },
        new object[] { "Mixto", 8 },
        new object[] { "SinTurno", 8 }
    };

    private static Shift? ShiftFor(string kind) => kind switch
    {
        "Regular" => Regular(),
        "Nocturno" => AttendanceRegressionFixtures.CreateNightShift(),
        "Flexible" => Shift.Create("Flexible 8h", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Flexible),
        "MultiInterval" => AttendanceRegressionFixtures.CreateMultiIntervalShift(),
        "Partido" => Shift.Create("Partido 9-13 y 15-19", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(4), ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(15, 0, 0), secondBlockEndTime: new TimeSpan(19, 0, 0)),
        "Mixto" => AttendanceRegressionFixtures.CreateMixedShift(),
        _ => null
    };

    [Theory]
    [MemberData(nameof(Processors))]
    public async Task Matrix_EveryProcessor_AppliesAndPreservesManualDecisions(string kind, int startHour)
    {
        var h = new AttendanceRecalculationHarness();
        var shift = ShiftFor(kind);
        if (shift != null) h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(CreateEmployee(Emp, shift));
        // Sin turno: sin horario ni turnos para autodetectar, la jornada se procesa con ProcessNoShift.

        var start = Tuesday.AddHours(startHour);
        Punch(h, Emp, start);
        var chosen = Punch(h, Emp, start.AddMinutes(25));
        Punch(h, Emp, start.AddHours(kind == "Partido" ? 4 : 8));
        await h.RecalculateAsync(Tuesday, Tuesday);

        var decisions = h.Decisions();
        var day = DateOnly.FromDateTime(Tuesday);
        (await Assign(h, chosen, Tuesday, "Entrada")).IsSuccess.Should().BeTrue($"{kind}: asignación");
        (await new SetDailyOvertimeAuthorizationCommandHandler(decisions).Handle(new SetDailyOvertimeAuthorizationCommand(Emp.Value, day, false, false), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await new SetDailyRestDayOverrideCommandHandler(h.DailyRepository, decisions).Handle(new SetDailyRestDayOverrideCommand(h.Daily(Emp, Tuesday)!.Id.Value, true), CancellationToken.None)).Should().BeTrue();

        await RecalculateAroundAsync(h, Tuesday);

        var daily = h.Daily(Emp, Tuesday)!;
        daily.CheckInRecordId.Should().Be(chosen.Id, $"{kind}: la asignación manual se aplica");
        daily.OvertimeAuthorized.Should().BeFalse($"{kind}: la autorización de extras se aplica");
        daily.IsRestDay.Should().BeTrue($"{kind}: el descanso manual se aplica");
        daily.HasManualAdjustments.Should().BeTrue();
    }

    #endregion

    #region Captura manual

    private static RegisterManualAttendanceCommandHandler ManualCapture(AttendanceRecalculationHarness h) =>
        new(h.AttendanceRepository, h.UnitOfWork, AttendanceSystem.Application.Abstractions.DefaultTimeNormalizer.Instance);

    private static Task<Result> CaptureAsync(AttendanceRecalculationHarness h, DateTime civil) =>
        ManualCapture(h).Handle(new RegisterManualAttendanceCommand(Emp.Value, civil, AttendanceRegressionFixtures.TimeZoneMexicoCity), CancellationToken.None);

    [Fact]
    public async Task ManualCapture_OnCalculatedDay_IsPlacedByNormalRules_WithoutManualDecision()
    {
        var h = RegularWeek(out _);
        await h.RecalculateAsync(Tuesday, Tuesday);

        var result = await CaptureAsync(h, Tuesday.AddHours(18).AddMinutes(30));
        result.IsSuccess.Should().BeTrue(result.IsSuccess ? string.Empty : result.Error);
        await h.RecalculateAsync(Tuesday, Tuesday);

        var manual = h.Store.Records.Where(r => r.EmployeeId == Emp && r.Source == "Manual").Should().ContainSingle().Subject;
        h.Daily(Emp, Tuesday)!.CheckOutRecordId.Should().Be(manual.Id, "la jornada ya calculada no impide considerar el marcaje");
        h.Store.Adjustments.Should().BeEmpty("la captura no fuerza ninguna posición");
    }

    [Fact]
    public async Task ManualCapture_IsRejected_WhenAPunchExistsInTheSameMinute()
    {
        var h = RegularWeek(out _);

        var duplicate = await CaptureAsync(h, Tuesday.AddHours(16).AddMinutes(5).AddSeconds(40));
        var repeated = await CaptureAsync(h, Tuesday.AddHours(10));
        var again = await CaptureAsync(h, Tuesday.AddHours(10));

        duplicate.IsSuccess.Should().BeFalse("ya existe el marcaje de dispositivo de las 16:05");
        repeated.IsSuccess.Should().BeTrue();
        again.IsSuccess.Should().BeFalse();
        h.Store.Records.Where(r => r.EmployeeId == Emp && r.Source == "Manual").Should().ContainSingle();
    }

    [Fact]
    public async Task ManualCapture_AfterMidnight_BelongsToThePreviousNightJornada()
    {
        var h = new AttendanceRecalculationHarness();
        var night = AttendanceRegressionFixtures.CreateNightShift();
        h.Store.Shifts.Add(night);
        h.Store.Employees.Add(CreateEmployee(Emp, night));
        var checkIn = Punch(h, Emp, Monday.AddHours(21).AddMinutes(55));
        await h.RecalculateAsync(Monday, Tuesday);

        // Salida olvidada: se captura con la fecha civil del marcaje (martes) y se recalcula ese día, como la UI.
        var result = await CaptureAsync(h, Tuesday.AddHours(6).AddMinutes(10));
        result.IsSuccess.Should().BeTrue(result.IsSuccess ? string.Empty : result.Error);
        await h.RecalculateAsync(Tuesday, Tuesday);

        var manual = h.Store.Records.Single(r => r.Source == "Manual");
        var monday = h.Daily(Emp, Monday)!;
        monday.CheckInRecordId.Should().Be(checkIn.Id);
        monday.CheckOutRecordId.Should().Be(manual.Id, "el recálculo del martes incluye la jornada nocturna del lunes que alcanza el marcaje");
        h.Daily(Emp, Tuesday)?.CheckInRecordId.Should().NotBe(manual.Id);
    }

    #endregion
}
