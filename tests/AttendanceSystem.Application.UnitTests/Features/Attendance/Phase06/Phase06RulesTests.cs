using System;
using System.Linq;
using System.Threading.Tasks;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Phase06;

/// <summary>
/// Reglas de cálculo con el orquestador y los procesadores reales:
/// ventanas continuas (24 h, sin consumir la entrada siguiente), intervalos abiertos, configuración diaria
/// de mixtos, extras brutas/efectivas con autorización diaria y tope de período estable, y vigencias
/// laborales (reingreso, cambio de horario).
/// </summary>
public class Phase06RulesTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static readonly DateTime Tuesday = Monday.AddDays(1);
    private static readonly DateTime Wednesday = Monday.AddDays(2);
    private static readonly EmployeeId Emp = EmployeeId.From("EMP-RUL-001");

    private static Employee CreateEmployee(Shift? schedule, bool overtimeAuthorized = true,
        OvertimeCapType capType = OvertimeCapType.None, double? cap = null) =>
        Employee.Create(Emp, "Empleado", "Sintetico", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            schedule?.ShiftType ?? ShiftType.Rotativo, schedule?.Id, restDays: Array.Empty<WeekDay>(),
            overtimeAuthorized: overtimeAuthorized, overtimeCapType: capType, overtimeCapMinutes: cap);

    private static AttendanceRecalculationHarness Harness(Shift shift, Employee employee)
    {
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(employee);
        return h;
    }

    private static AttendanceRecord Punch(AttendanceRecalculationHarness h, DateTime civil)
    {
        var record = AttendanceRegressionFixtures.CreatePunch(Emp, civil, CheckType.CheckIn);
        h.Store.Records.Add(record);
        return record;
    }

    private static DailyAttendance? Day(AttendanceRecalculationHarness h, DateTime date) =>
        h.Store.Dailies.SingleOrDefault(d => d.EmployeeId == Emp && d.Date.Date == date.Date);

    #region F06-01 ventanas

    [Fact]
    public async Task Flexible20h_MissingExit_DoesNotConsumeTheNextJornadaEntry()
    {
        // 20 h desde las 06:00: la ventana llegaría a las 06:00 del martes, pero la entrada del martes se
        // acepta desde las 04:00 (2 h de anticipación); el marcaje de las 05:30 pertenece al martes.
        var shift = Shift.Create("Flexible 20h", new TimeSpan(6, 0, 0), 15, TimeSpan.FromHours(20), ShiftType.Flexible);
        var h = Harness(shift, CreateEmployee(shift));
        Punch(h, Monday.AddHours(6));
        var tuesdayEntry = Punch(h, Tuesday.AddHours(5).AddMinutes(30));
        Punch(h, Tuesday.AddHours(23));

        await h.RecalculateAsync(Monday, Tuesday);

        Day(h, Monday)!.CheckOutRecordId.Should().BeNull("la salida faltante del lunes no toma la entrada del martes");
        Day(h, Monday)!.MissingCheckOut.Should().BeTrue();
        Day(h, Tuesday)!.CheckInRecordId.Should().Be(tuesdayEntry.Id);
    }

    [Fact]
    public void WindowPolicy_AnchorsOnScheduledStart_AndAddsTheAllowanceToTheShiftDuration()
    {
        var scheduled = Monday.AddHours(8);
        // Entrada anticipada: no acorta la ventana.
        JornadaWindowPolicy.ExitWindowEnd(scheduled.AddMinutes(-10), scheduled, TimeSpan.FromHours(24), null).Should().Be(scheduled.AddHours(28));
        // Entrada tardía: la ventana corre desde la entrada real.
        JornadaWindowPolicy.ExitWindowEnd(scheduled.AddHours(1), scheduled, TimeSpan.FromHours(8), null).Should().Be(scheduled.AddHours(13));
        // Sin duración definida: 24 h desde la entrada.
        JornadaWindowPolicy.ExitWindowEnd(scheduled, null, TimeSpan.Zero, null).Should().Be(scheduled.AddHours(24));
        // La jornada siguiente limita, pero nunca por debajo de la duración programada.
        JornadaWindowPolicy.ExitWindowEnd(scheduled, scheduled, TimeSpan.FromHours(8), scheduled.AddHours(10)).Should().BeBefore(scheduled.AddHours(10));
        JornadaWindowPolicy.ExitWindowEnd(scheduled, scheduled, TimeSpan.FromHours(24), scheduled.AddHours(22)).Should().Be(scheduled.AddHours(24));
    }

    [Fact]
    public async Task Guard24h_EarlyEntry_AndLateExit_IsACompleteJornada()
    {
        // Guardia 24x24: martes de descanso, la salida de 20 min extra pertenece al lunes.
        var shift = AttendanceRegressionFixtures.CreateGuard24hShift();
        var employee = Employee.Create(Emp, "Empleado", "Sintetico", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: new[] { WeekDay.Martes }, overtimeAuthorized: true);
        var h = Harness(shift, employee);
        var entry = Punch(h, Monday.AddHours(7).AddMinutes(50));
        Punch(h, Monday.AddHours(20));
        var exit = Punch(h, Tuesday.AddHours(8).AddMinutes(10));

        await h.RecalculateAsync(Monday, Tuesday);

        var day = Day(h, Monday)!;
        day.CheckInRecordId.Should().Be(entry.Id);
        day.CheckOutRecordId.Should().Be(exit.Id, "24 h 20 min está dentro de la duración del turno + 4 h");
    }

    [Theory]
    [InlineData(ShiftType.Flexible)]
    public async Task EightHourShift_WithSixHoursOvertime_KeepsTheRealExit_AndLunchAsIntermediate(ShiftType type)
    {
        var shift = Shift.Create($"{type} 8h", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), type);
        var h = Harness(shift, CreateEmployee(shift));
        Punch(h, Monday.AddHours(8));
        Punch(h, Monday.AddHours(14));
        Punch(h, Monday.AddHours(15));
        var exit = Punch(h, Monday.AddHours(22));

        await h.RecalculateAsync(Monday, Monday);

        Day(h, Monday)!.CheckOutRecordId.Should().Be(exit.Id, "un marcaje del mismo día siempre puede ser la salida");
    }

    [Theory]
    [InlineData(ShiftType.Flexible)]
    public async Task SingleInterval_WorkedMinutes_UseCivilTimeOnBothEnds(ShiftType type)
    {
        // Antes la salida quedaba en UTC y la entrada en hora civil: en UTC-6 sumaba 6 h trabajadas.
        var shift = Shift.Create($"{type} 8h", new TimeSpan(8, 0, 0), 15, TimeSpan.FromHours(8), type);
        var h = Harness(shift, CreateEmployee(shift));
        Punch(h, Monday.AddHours(8));
        Punch(h, Monday.AddHours(17));

        await h.RecalculateAsync(Monday, Monday);

        Day(h, Monday)!.TotalWorkedMinutes.Should().Be(540);
    }

    #endregion

    #region F06-02 intervalos abiertos

    [Fact]
    public async Task MultiInterval_OddPunches_ReportOpenInterval_AndCountOnlyClosedIntervals()
    {
        var shift = AttendanceRegressionFixtures.CreateMultiIntervalShift();
        var h = Harness(shift, CreateEmployee(shift));
        Punch(h, Monday.AddHours(8));
        Punch(h, Monday.AddHours(12));
        Punch(h, Monday.AddHours(13));

        await h.RecalculateAsync(Monday, Monday);

        var day = Day(h, Monday)!;
        day.HasOpenInterval.Should().BeTrue();
        day.MissingCheckOut.Should().BeTrue();
        day.TotalWorkedMinutes.Should().Be(240);
        day.AttendanceNote.Should().Contain("Intervalo abierto");

        // El recálculo reconstruye la incidencia, no la acumula.
        await h.RecalculateAsync(Monday, Monday);
        Day(h, Monday)!.CalculationIssue.Should().Be(day.CalculationIssue);
    }

    [Fact]
    public async Task MultiInterval_DoubleTap_IsDiscarded_AndOriginalTypeIsKeptApartFromInference()
    {
        var shift = AttendanceRegressionFixtures.CreateMultiIntervalShift();
        var h = Harness(shift, CreateEmployee(shift));
        Punch(h, Monday.AddHours(8));
        var doubleTap = Punch(h, Monday.AddHours(8).AddSeconds(5));
        var deviceSaysCheckIn = Punch(h, Monday.AddHours(16));

        await h.RecalculateAsync(Monday, Monday);

        var day = Day(h, Monday)!;
        day.TotalWorkedMinutes.Should().Be(480, "el doble toque de 5 s no abre un intervalo");
        day.HasOpenInterval.Should().BeFalse();
        h.Store.Assignments.Single(a => a.Id == doubleTap.Id).Role.Should().Be(PunchAssignmentRole.DiscardedDoubleTap);
        deviceSaysCheckIn.CheckType.Should().Be(CheckType.CheckOut, "la inferencia lo trata como salida");
        deviceSaysCheckIn.OriginalCheckType.Should().Be(CheckType.CheckIn, "el tipo enviado por el dispositivo se conserva");
    }

    [Fact]
    public async Task MultiInterval_EvenPunches_HaveNoIssue()
    {
        var shift = AttendanceRegressionFixtures.CreateMultiIntervalShift();
        var h = Harness(shift, CreateEmployee(shift));
        Punch(h, Monday.AddHours(8));
        Punch(h, Monday.AddHours(12));
        Punch(h, Monday.AddHours(13));
        Punch(h, Monday.AddHours(17));

        await h.RecalculateAsync(Monday, Monday);

        var day = Day(h, Monday)!;
        day.HasOpenInterval.Should().BeFalse();
        day.CalculationIssue.Should().BeNull();
        day.TotalWorkedMinutes.Should().Be(480);
    }

    #endregion

    #region F06-04 mixtos

    [Fact]
    public async Task Mixed_UsesTheDailyStartTime_NotTheBaseShiftStart()
    {
        var shift = AttendanceRegressionFixtures.CreateMixedShift();
        var h = Harness(shift, CreateEmployee(shift));
        Punch(h, Wednesday.AddHours(14).AddMinutes(5));
        Punch(h, Wednesday.AddHours(22));

        await h.RecalculateAsync(Wednesday, Wednesday);

        var day = Day(h, Wednesday)!;
        day.ScheduledCheckIn.Should().Be(new TimeSpan(14, 0, 0));
        day.LateMinutes.Should().Be(0, "el vespertino de las 14:00 no se evalúa contra las 08:00 del turno base");
        day.ShiftId.Should().Be(shift.Id, "la jornada conserva la identidad del turno mixto");
    }

    [Fact]
    public async Task Mixed_FlexibleDay_UsesTheDailyDurationForItsExitWindow()
    {
        // Turno base de 8 h (ventana 12 h); el lunes es flexible de 12 h (ventana 16 h).
        var days = new[] { new ShiftDay(DayOfWeek.Monday, new TimeSpan(6, 0, 0), TimeSpan.FromHours(12), ShiftType.Flexible) };
        var shift = Shift.Create("Mixto flexible", new TimeSpan(6, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Mixto, days);
        var h = Harness(shift, CreateEmployee(shift));
        Punch(h, Monday.AddHours(6));
        Punch(h, Monday.AddHours(17));
        var exit = Punch(h, Monday.AddHours(21));

        await h.RecalculateAsync(Monday, Monday);

        Day(h, Monday)!.CheckOutRecordId.Should().Be(exit.Id, "la ventana se calcula con las 12 h del día, no con las 8 h del turno base");
    }

    [Fact]
    public async Task Mixed_SplitDay_ReportsIncompleteConfiguration()
    {
        var days = new[] { new ShiftDay(DayOfWeek.Monday, new TimeSpan(9, 0, 0), TimeSpan.FromHours(8), ShiftType.Partido) };
        var shift = Shift.Create("Mixto con partido", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Mixto, days);
        var h = Harness(shift, CreateEmployee(shift));
        Punch(h, Monday.AddHours(9));
        Punch(h, Monday.AddHours(17));

        await h.RecalculateAsync(Monday, Monday);

        Day(h, Monday)!.CalculationIssue.Should().Contain("segundo bloque", "un día partido sin bloques no se omite en silencio");
    }

    #endregion

    #region F06-06 / F06-07 extras

    [Fact]
    public async Task Overtime_KeepsRawWhenNotAuthorized_AndDailyOverrideAuthorizes()
    {
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        var h = Harness(shift, CreateEmployee(shift, overtimeAuthorized: false));
        foreach (var day in new[] { Monday, Tuesday })
        {
            Punch(h, day.AddHours(8));
            Punch(h, day.AddHours(19));
        }
        var adjustment = AttendanceAdjustment.Create(Emp, Tuesday, "rh");
        adjustment.SetOvertimeConfiguration(true, false, "rh");
        h.Store.Adjustments.Add(adjustment);

        await h.RecalculateAsync(Monday, Tuesday);

        Day(h, Monday)!.RawOvertimeMinutes.Should().BeGreaterThan(0, "el excedente bruto se conserva sin autorización");
        Day(h, Monday)!.OvertimeMinutes.Should().Be(0);
        Day(h, Tuesday)!.OvertimeMinutes.Should().Be(Day(h, Tuesday)!.RawOvertimeMinutes, "la autorización diaria rige sobre la global");
        Day(h, Tuesday)!.OvertimeMinutes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Overtime_GloballyAuthorized_ButForbiddenForOneDay_GeneratesNoOvertimeThatDay()
    {
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        var h = Harness(shift, CreateEmployee(shift, overtimeAuthorized: true));
        foreach (var day in new[] { Monday, Tuesday })
        {
            Punch(h, day.AddHours(8));
            Punch(h, day.AddHours(19));
        }
        var adjustment = AttendanceAdjustment.Create(Emp, Tuesday, "rh");
        adjustment.SetOvertimeConfiguration(false, false, "rh");
        h.Store.Adjustments.Add(adjustment);

        await h.RecalculateAsync(Monday, Tuesday);

        Day(h, Monday)!.OvertimeMinutes.Should().BeGreaterThan(0);
        Day(h, Tuesday)!.OvertimeMinutes.Should().Be(0, "la prohibición diaria rige sobre la autorización global");
        Day(h, Tuesday)!.RawOvertimeMinutes.Should().BeGreaterThan(0, "el excedente bruto queda visible para RH");
    }

    [Fact]
    public async Task PeriodCap_DoesNotDependOnTheRecalculatedRange()
    {
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        var h = Harness(shift, CreateEmployee(shift, capType: OvertimeCapType.Period, cap: 150));
        foreach (var day in new[] { Monday, Tuesday, Wednesday })
        {
            Punch(h, day.AddHours(8));
            Punch(h, day.AddHours(18));
        }

        await h.RecalculateAsync(Monday, Wednesday);
        var full = new[] { Monday, Tuesday, Wednesday }.Select(d => Day(h, d)!.OvertimeMinutes).ToArray();
        full.Sum().Should().BeLessThanOrEqualTo(150);
        full[2].Should().BeLessThan(full[0], "el tope del período se agota en orden cronológico");

        // Recalcular sólo el miércoles da el mismo resultado: el período se completa siempre.
        await h.RecalculateAsync(Wednesday, Wednesday);
        Day(h, Wednesday)!.OvertimeMinutes.Should().Be(full[2]);
    }

    [Fact]
    public async Task PeriodCap_ResetsAtTheConfiguredFortnight()
    {
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        var h = Harness(shift, CreateEmployee(shift, capType: OvertimeCapType.Period, cap: 60));
        var config = SystemConfiguration.CreateDefault();
        config.UpdateWorkPeriodSettings(WorkPeriodMode.Fortnightly, DayOfWeek.Monday, 1, 16, 1);
        h.Configuration = config;
        var fifteenth = new DateTime(2026, 9, 15);
        var sixteenth = new DateTime(2026, 9, 16);
        foreach (var day in new[] { fifteenth, sixteenth })
        {
            Punch(h, day.AddHours(8));
            Punch(h, day.AddHours(18));
        }

        await h.RecalculateAsync(sixteenth, sixteenth);
        await h.RecalculateAsync(fifteenth, fifteenth);

        Day(h, fifteenth)!.OvertimeMinutes.Should().Be(60);
        Day(h, sixteenth)!.OvertimeMinutes.Should().Be(60, "la segunda quincena empieza con el tope completo");
    }

    [Fact]
    public async Task PeriodCap_RecalculatingOneDay_SeesEarlierWeeksOfTheSamePeriod()
    {
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        var h = Harness(shift, CreateEmployee(shift, capType: OvertimeCapType.Period, cap: 60));
        var config = SystemConfiguration.CreateDefault();
        config.UpdateWorkPeriodSettings(WorkPeriodMode.Fortnightly, DayOfWeek.Monday, 1, 16, 1);
        h.Configuration = config;
        var first = new DateTime(2026, 9, 1);     // martes, primera semana de la quincena
        var fourteenth = new DateTime(2026, 9, 14); // lunes, otra semana de la misma quincena
        foreach (var day in new[] { first, fourteenth })
        {
            Punch(h, day.AddHours(8));
            Punch(h, day.AddHours(18));
        }

        await h.RecalculateAsync(first, fourteenth);
        Day(h, first)!.OvertimeMinutes.Should().Be(60);
        Day(h, fourteenth)!.OvertimeMinutes.Should().Be(0, "el tope de la quincena ya se agotó el día 1");

        await h.RecalculateAsync(fourteenth, fourteenth);
        Day(h, fourteenth)!.OvertimeMinutes.Should().Be(0, "recalcular sólo el día 14 considera el resto de la quincena");
    }

    #endregion

    [Theory]
    [InlineData(8, 0, 18, 0, 0, 120)]   // jornada normal + 2 h: sólo el excedente es extra
    [InlineData(10, 0, 18, 0, 120, 0)]  // llega a las 10:00: el retardo se registra como en cualquier día
    public async Task RestDayWorked_CountsAsANormalDay_AndIsFlagged(int inH, int inM, int outH, int outM, int expectedLate, int expectedRawOvertime)
    {
        // Decisión de cierre F06-06: el descanso trabajado se calcula como un día normal (retardos incluidos), sólo
        // es extra lo que excede la duración del horario y la jornada queda marcada como descanso trabajado.
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        var employee = Employee.Create(Emp, "Empleado", "Sintetico", null, null, new DateTime(2024, 1, 1), Gender.Male,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: new[] { WeekDay.Lunes }, overtimeAuthorized: true);
        var h = Harness(shift, employee);
        Punch(h, Monday.AddHours(inH).AddMinutes(inM));
        Punch(h, Monday.AddHours(outH).AddMinutes(outM));

        await h.RecalculateAsync(Monday, Monday);

        var day = Day(h, Monday)!;
        day.IsRestDay.Should().BeTrue();
        day.WorkedOnRestDay.Should().BeTrue();
        day.LateMinutes.Should().Be(expectedLate);
        day.RawOvertimeMinutes.Should().Be(expectedRawOvertime);
        day.IsAbsent.Should().BeFalse();
    }

    #region F06-08 vigencias

    [Fact]
    public async Task Rehire_GapDaysAreNotCalculated()
    {
        var shift = AttendanceRegressionFixtures.CreateRegularShift();
        var employee = CreateEmployee(shift);
        employee.Terminate(Monday);
        employee.Rehire(Wednesday);
        var h = Harness(shift, employee);
        foreach (var day in new[] { Monday, Tuesday, Wednesday })
        {
            Punch(h, day.AddHours(8));
            Punch(h, day.AddHours(16));
        }

        await h.RecalculateAsync(Monday, Wednesday);

        Day(h, Monday).Should().NotBeNull();
        Day(h, Tuesday).Should().BeNull("durante la baja no hay jornada ni falta");
        Day(h, Wednesday).Should().NotBeNull();
        employee.EmploymentPeriods.Should().HaveCount(2);
    }

    [Fact]
    public async Task ScheduleChange_AppliesOnlyFromItsEffectiveDate()
    {
        var morning = AttendanceRegressionFixtures.CreateRegularShift();
        var afternoon = Shift.Create("Vespertino 14-22", new TimeSpan(14, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Vespertino);
        var employee = CreateEmployee(morning);
        employee.RecordAssignment(Wednesday, afternoon.Id, ShiftType.Vespertino, employee.BranchId);
        var h = Harness(morning, employee);
        h.Store.Shifts.Add(afternoon);
        Punch(h, Monday.AddHours(8));
        Punch(h, Monday.AddHours(16));
        Punch(h, Wednesday.AddHours(14));
        Punch(h, Wednesday.AddHours(22));

        await h.RecalculateAsync(Monday, Wednesday);

        Day(h, Monday)!.ShiftId.Should().Be(morning.Id, "recalcular el pasado usa el horario vigente entonces");
        Day(h, Wednesday)!.ShiftId.Should().Be(afternoon.Id);
    }

    #endregion
}
