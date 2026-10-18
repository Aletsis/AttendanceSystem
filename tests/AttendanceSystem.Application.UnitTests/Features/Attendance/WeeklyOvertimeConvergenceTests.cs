using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

public class WeeklyOvertimeConvergenceTests
{
    private readonly Employee _employeeWithCap;
    private readonly Shift _shift;

    public WeeklyOvertimeConvergenceTests()
    {
        _shift = Shift.Create("Jornada 8h", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);

        _employeeWithCap = Employee.Create(
            id: EmployeeId.From("EMP-CAP-1"),
            firstName: "Cap",
            lastName: "Tester",
            email: null,
            phoneNumber: null,
            hireDate: new DateTime(2025, 1, 1),
            gender: Gender.Male,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: ShiftType.Matutino,
            scheduleId: _shift.Id,
            overtimeAuthorized: true,
            overtimeCapType: OvertimeCapType.Daily,
            overtimeCapMinutes: 60);
    }

    [Fact]
    public void GetWeekEnd_CalculatesCorrectEndOfWeek_ForDifferentWeeklyStartDays()
    {
        // Arrange (H18)
        // Miércoles 14 de octubre de 2026
        var wednesday = new DateTime(2026, 10, 14);

        // Act & Assert
        // Si la semana inicia el Lunes (Monday), el fin de semana es Domingo (2026-10-18)
        WeeklyHoursCalculationHelper.GetWeekEnd(wednesday, DayOfWeek.Monday)
            .Should().Be(new DateTime(2026, 10, 18));

        // Si la semana inicia el Domingo (Sunday), el fin de semana es Sábado (2026-10-17)
        WeeklyHoursCalculationHelper.GetWeekEnd(wednesday, DayOfWeek.Sunday)
            .Should().Be(new DateTime(2026, 10, 17));

        // Si la semana inicia el Sábado (Saturday), el fin de semana es Viernes (2026-10-16)
        WeeklyHoursCalculationHelper.GetWeekEnd(wednesday, DayOfWeek.Saturday)
            .Should().Be(new DateTime(2026, 10, 16));

        // Si la fecha cae exactamente en el último día de la semana (Domingo con inicio Lunes), devuelve ese mismo Domingo
        var sunday = new DateTime(2026, 10, 18);
        WeeklyHoursCalculationHelper.GetWeekEnd(sunday, DayOfWeek.Monday)
            .Should().Be(new DateTime(2026, 10, 18));
    }

    [Fact]
    public void CalculateEffectiveOvertime_DoesNotCompoundCappedOvertime_OnSubsequentRecalculation()
    {
        // Arrange (H18 RawOvertime baseline)
        var date = new DateTime(2026, 10, 12); // Lunes
        var daily = DailyAttendance.Create(
            _employeeWithCap.Id,
            date,
            _shift,
            checkIn: date.AddHours(8),
            checkOut: date.AddHours(18), // 10 horas trabajadas = 600 min
            isRestDay: false);

        // Supongamos que la jornada diaria regular es de 8h (480 min), por lo que RawOvertime = 120 min
        daily.SetCalculatedOvertime(120);
        daily.RawOvertimeMinutes.Should().Be(120);

        var list = new List<DailyAttendance> { daily };

        // Act - Primera pasada: Cálculo efectivo aplica tope diario de 60 min
        var pass1 = WeeklyHoursCalculationHelper.CalculateEffectiveOvertime(list, _employeeWithCap, DayOfWeek.Monday);
        pass1[daily.Id].Should().Be(60);

        // Aplicamos el overtime efectivo calculado
        daily.SetCalculatedOvertime(pass1[daily.Id]);
        daily.OvertimeMinutes.Should().Be(60);
        // La línea base inmutable se mantiene intacta en 120 (H18)
        daily.RawOvertimeMinutes.Should().Be(120);

        // Act - Segunda pasada (Recálculo idempotente)
        var pass2 = WeeklyHoursCalculationHelper.CalculateEffectiveOvertime(list, _employeeWithCap, DayOfWeek.Monday);

        // Assert: El resultado sigue siendo exactamente 60, no se acumuló ni encogió el tope sobre el tope anterior
        pass2[daily.Id].Should().Be(60);
    }
}
