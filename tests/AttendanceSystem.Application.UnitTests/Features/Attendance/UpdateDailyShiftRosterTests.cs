using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.UpdateDailyShift;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

public class UpdateDailyShiftRosterTests
{
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock = new();
    private readonly Mock<IShiftRepository> _shiftRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IAttendanceRepository> _attendanceRepoMock = new();
    private readonly Mock<IEmployeeRepository> _employeeRepoMock = new();
    private readonly Mock<IAttendanceAdjustmentRepository> _adjustmentRepoMock = new();
    private readonly Mock<IShiftRosterRepository> _rosterRepoMock = new();

    private readonly Shift _oldShift;
    private readonly Shift _newShift;
    private readonly Employee _employeeWithMultipleRestDays;

    public UpdateDailyShiftRosterTests()
    {
        _oldShift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        _newShift = Shift.Create("Vespertino", new TimeSpan(14, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Vespertino);

        // Empleado con dos días de descanso: Miércoles y Domingo
        _employeeWithMultipleRestDays = Employee.Create(
            id: EmployeeId.From("EMP-ROST-1"),
            firstName: "Roster",
            lastName: "Tester",
            email: null,
            phoneNumber: null,
            hireDate: new DateTime(2025, 1, 1),
            gender: Gender.Male,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: ShiftType.Matutino,
            scheduleId: _oldShift.Id,
            restDays: new[] { WeekDay.Miercoles, WeekDay.Domingo });

        _employeeRepoMock.Setup(r => r.GetByIdAsync(_employeeWithMultipleRestDays.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeWithMultipleRestDays);

        _shiftRepoMock.Setup(r => r.GetByIdAsync(_newShift.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_newShift);

        _attendanceRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord>());

        _adjustmentRepoMock.Setup(r => r.GetByEmployeeAndDateAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceAdjustment?)null);

        _rosterRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster>());
    }

    [Fact]
    public async Task Handle_PersistsShiftOverrideAsAuditedDecision_AndRecalculatesWithTheCommonEngine()
    {
        // H28 / F02-05 / F05-02: el cambio de turno es una decisión persistente y auditada; el motor común
        // reconstruye la jornada respetando los RestDays múltiples del contrato.
        var wednesday = new DateOnly(2026, 10, 14);
        var h = new Phase02.AttendanceRecalculationHarness();
        h.Store.Shifts.AddRange(new[] { _oldShift, _newShift });
        h.Store.Employees.Add(_employeeWithMultipleRestDays);

        var handler = new UpdateDailyShiftCommandHandler(_shiftRepoMock.Object, _employeeRepoMock.Object, h.Decisions());

        var result = await handler.Handle(new UpdateDailyShiftCommand(_employeeWithMultipleRestDays.Id.Value, wednesday, _newShift.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsSuccess ? string.Empty : result.Error);
        var adjustment = h.Store.Adjustments.Single();
        adjustment.ShiftIdOverride.Should().Be(_newShift.Id);
        h.Store.AdjustmentHistory.Should().ContainSingle(e => e.Action == "Turno" && e.ChangedBy == h.CurrentUser);
        h.Store.ConsolidationRequests.Should().ContainSingle(r => r.EmployeeId == _employeeWithMultipleRestDays.Id);

        var daily = h.Daily(_employeeWithMultipleRestDays.Id, wednesday.ToDateTime(TimeOnly.MinValue))!;
        daily.ShiftName.Should().Be(_newShift.Name);
        daily.IsRestDay.Should().BeTrue("el miércoles es descanso en los RestDays del contrato");
    }

    [Fact]
    public void AttendanceShiftResolutionService_PrecedenceOrder_AdjustmentBeatsRosterBeatsContract()
    {
        // Arrange (H28 Centralized resolution)
        var date = new DateTime(2026, 10, 14); // Miércoles (Descanso en contrato)
        var rosterShift = Shift.Create("Roster Shift", new TimeSpan(7, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        var adjustmentShift = Shift.Create("Adjustment Shift", new TimeSpan(12, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Vespertino);

        var allShifts = new Dictionary<ShiftId, Shift>
        {
            [_oldShift.Id] = _oldShift,
            [rosterShift.Id] = rosterShift,
            [adjustmentShift.Id] = adjustmentShift
        };

        // Escenario 1: Sin roster ni ajuste -> Contrato gana (Descanso = true porque es Miércoles)
        var res1 = AttendanceShiftResolutionService.Resolve(
            _employeeWithMultipleRestDays,
            date,
            null,
            null,
            allShifts);
        res1.IsRestDay.Should().BeTrue();
        res1.Shift!.Id.Should().Be(_oldShift.Id);

        // Escenario 2: Con Roster activo -> Roster gana sobre contrato (isRestDay: false, Turno: rosterShift)
        var rosterEntry = ShiftRoster.Create(_employeeWithMultipleRestDays.Id, date, rosterShift.Id, isRestDay: false);
        var res2 = AttendanceShiftResolutionService.Resolve(
            _employeeWithMultipleRestDays,
            date,
            rosterEntry,
            null,
            allShifts);
        res2.IsRestDay.Should().BeFalse();
        res2.Shift!.Id.Should().Be(rosterShift.Id);

        // Escenario 3: Con Ajuste manual de RH -> Ajuste gana sobre Roster y sobre Contrato
        var adjustment = AttendanceAdjustment.Create(_employeeWithMultipleRestDays.Id, date, "Ajuste de Gerencia");
        adjustment.SetShiftOverride(adjustmentShift.Id, "AdminRH", "Override de emergencia");
        adjustment.SetRestDayOverride(false, "AdminRH", "Trabajo en descanso");

        var res3 = AttendanceShiftResolutionService.Resolve(
            _employeeWithMultipleRestDays,
            date,
            rosterEntry,
            adjustment,
            allShifts);
        res3.IsRestDay.Should().BeFalse();
        res3.Shift!.Id.Should().Be(adjustmentShift.Id);
    }
}
