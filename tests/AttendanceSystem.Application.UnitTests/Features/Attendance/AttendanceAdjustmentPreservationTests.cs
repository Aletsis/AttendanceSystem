using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

public class AttendanceAdjustmentPreservationTests
{
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock = new();
    private readonly Mock<IAttendanceRepository> _attendanceRepoMock = new();
    private readonly Mock<IEmployeeRepository> _employeeRepoMock = new();
    private readonly Mock<IShiftRepository> _shiftRepoMock = new();
    private readonly Mock<IShiftRosterRepository> _rosterRepoMock = new();
    private readonly Mock<ISystemConfigurationRepository> _systemConfigRepoMock = new();
    private readonly Mock<IAttendanceAdjustmentRepository> _adjustmentRepoMock = new();
    private readonly Mock<IAttendanceConsolidationRepository> _consolidationRepoMock = new();
    private readonly Mock<IAttendanceCalculationLockService> _lockServiceMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ISender> _senderMock = new();
    private readonly Mock<ILogger<ProcessDailyAttendanceCommandHandler>> _loggerMock = new();

    private readonly Shift _morningShift;
    private readonly Shift _customShift;
    private readonly Employee _employee;
    private readonly DateTime _date = new(2026, 10, 14);

    public AttendanceAdjustmentPreservationTests()
    {
        _morningShift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
        _customShift = Shift.Create("Vespertino Especial", new TimeSpan(14, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Vespertino);

        _employee = Employee.Create(
            id: EmployeeId.From("EMP-ADJ-1"),
            firstName: "Ajuste",
            lastName: "Tester",
            email: "ajuste@test.com",
            phoneNumber: null,
            hireDate: new DateTime(2025, 1, 1),
            gender: Gender.Male,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: ShiftType.Matutino,
            scheduleId: _morningShift.Id);

        _shiftRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Shift> { _morningShift, _customShift });

        _employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { _employee });

        _rosterRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster>());

        _attendanceRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord>());

        _dailyRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance>());

        _unitOfWorkMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());

        _lockServiceMock
            .Setup(l => l.ExecuteWithLockAsync(
                It.IsAny<IEnumerable<EmployeeId>>(),
                It.IsAny<Func<Task<int>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<EmployeeId>, Func<Task<int>>, CancellationToken>((_, func, _) => func());
    }

    private ProcessDailyAttendanceCommandHandler CreateHandler() =>
        new(
            _dailyRepoMock.Object,
            _attendanceRepoMock.Object,
            _employeeRepoMock.Object,
            _shiftRepoMock.Object,
            _rosterRepoMock.Object,
            _systemConfigRepoMock.Object,
            _adjustmentRepoMock.Object,
            _consolidationRepoMock.Object,
            _lockServiceMock.Object,
            _unitOfWorkMock.Object,
            _senderMock.Object,
            _loggerMock.Object);

    [Fact]
    public async Task Handle_WhenRestDayOverrideExistsInAdjustments_ShouldApplyOverrideDuringRecalculation()
    {
        // Arrange (H07)
        // El empleado tiene turno regular en miércoles (2026-10-14), pero RH forzó descanso mediante ajuste
        var adjustment = AttendanceAdjustment.Create(_employee.Id, _date, "Aprobación médica");
        adjustment.SetRestDayOverride(isRestDay: true, "AdminRH", "Aprobación médica");

        _adjustmentRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceAdjustment> { adjustment });

        var handler = CreateHandler();
        var command = new ProcessDailyAttendanceCommand(_date, _date, EmployeeId: _employee.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert: El comando de asistencia regular recibe IsRestDay == true y el Adjustment inyectado
        result.Should().Be(1);
        _senderMock.Verify(s => s.Send(
            It.Is<ProcessRegularAttendanceCommand>(c => c.IsRestDay == true && c.Adjustment != null && c.Adjustment.IsRestDayOverride == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenManualShiftOverrideExists_ShouldPrioritizeAdjustmentOverContract()
    {
        // Arrange (H07)
        // RH reasignó manualmente el turno a Vespertino para este día
        var adjustment = AttendanceAdjustment.Create(_employee.Id, _date, "Cambio de cobertura");
        adjustment.SetShiftOverride(_customShift.Id, "AdminRH", "Cobertura vespertina");

        _adjustmentRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceAdjustment> { adjustment });

        var handler = CreateHandler();
        var command = new ProcessDailyAttendanceCommand(_date, _date, EmployeeId: _employee.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert: Se despacha ProcessRegularAttendanceCommand con el turno Vespertino de ajuste, NO el Matutino de contrato
        result.Should().Be(1);
        _senderMock.Verify(s => s.Send(
            It.Is<ProcessRegularAttendanceCommand>(c => c.Shift.Id == _customShift.Id && c.Adjustment != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenLegacyDailyAttendanceHasRestDayOverride_ShouldHotMigrateToAdjustment()
    {
        // Arrange (H07 Migración en caliente)
        // Registro preexistente en DailyAttendance con override pero sin registro en AttendanceAdjustments
        var legacyDA = DailyAttendance.Create(_employee.Id, _date, _morningShift, null, null, isRestDay: true);
        legacyDA.SetRestDayOverride(true);

        _dailyRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { legacyDA });

        _adjustmentRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceAdjustment>());

        var handler = CreateHandler();
        var command = new ProcessDailyAttendanceCommand(_date, _date, EmployeeId: _employee.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert: El ajuste migrado fue agregado al repositorio durable
        result.Should().Be(1);
        _adjustmentRepoMock.Verify(r => r.AddAsync(
            It.Is<AttendanceAdjustment>(a => a.EmployeeId == _employee.Id && a.Date == _date.Date && a.IsRestDayOverride == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenManualPunchAssignmentExists_ShouldInjectPunchRecordIds()
    {
        // Arrange (H07)
        var inPunchId = AttendanceRecordId.CreateNew();
        var outPunchId = AttendanceRecordId.CreateNew();

        var adjustment = AttendanceAdjustment.Create(_employee.Id, _date, "Asignación manual de marcas");
        adjustment.SetCheckIn(inPunchId, _date.AddHours(8).AddMinutes(5), "AdminRH");
        adjustment.SetCheckOut(outPunchId, _date.AddHours(16).AddMinutes(5), "AdminRH");

        _adjustmentRepoMock.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceAdjustment> { adjustment });

        var handler = CreateHandler();
        var command = new ProcessDailyAttendanceCommand(_date, _date, EmployeeId: _employee.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert: El subcomando recibe el ajuste con las asignaciones intactas
        result.Should().Be(1);
        _senderMock.Verify(s => s.Send(
            It.Is<ProcessRegularAttendanceCommand>(c => c.Adjustment != null && c.Adjustment.CheckInRecordId == inPunchId && c.Adjustment.CheckOutRecordId == outPunchId),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
