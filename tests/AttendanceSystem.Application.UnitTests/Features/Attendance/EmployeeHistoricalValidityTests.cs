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

public class EmployeeHistoricalValidityTests
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

    private readonly Shift _shift;
    private readonly Employee _terminatedEmployee;
    private readonly DateTime _terminationDate = new(2026, 6, 30);

    public EmployeeHistoricalValidityTests()
    {
        _shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);

        _terminatedEmployee = Employee.Create(
            id: EmployeeId.From("EMP-BAJA-1"),
            firstName: "ExEmpleado",
            lastName: "Historico",
            email: "historico@test.com",
            phoneNumber: null,
            hireDate: new DateTime(2025, 1, 1),
            gender: Gender.Male,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: ShiftType.Matutino,
            scheduleId: _shift.Id);

        // Se da de baja con fecha efectiva 30 de junio de 2026
        _terminatedEmployee.Terminate(_terminationDate);

        _shiftRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Shift> { _shift });

        _employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { _terminatedEmployee });

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
    public async Task Handle_WhenEmployeeIsTerminated_ShouldProcessPastDatesPriorToTermination()
    {
        // Arrange (H08)
        // El empleado está en estado Baja hoy, pero recalculamos mayo 2026 (cuando estaba activo)
        var start = new DateTime(2026, 5, 1);
        var end = new DateTime(2026, 5, 5);

        var handler = CreateHandler();
        var command = new ProcessDailyAttendanceCommand(start, end, EmployeeId: _terminatedEmployee.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert: 5 días procesados normalmente
        result.Should().Be(5);
        _senderMock.Verify(s => s.Send(It.IsAny<ProcessRegularAttendanceCommand>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    [Fact]
    public async Task Handle_WhenRecalculatingAfterTerminationDate_ShouldSkipProcessingForTerminatedEmployee()
    {
        // Arrange (H08)
        // Recalculamos julio 2026 (después de la fecha de baja 2026-06-30)
        var start = new DateTime(2026, 7, 1);
        var end = new DateTime(2026, 7, 5);

        var handler = CreateHandler();
        var command = new ProcessDailyAttendanceCommand(start, end, EmployeeId: _terminatedEmployee.Id);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert: 0 días procesados porque no estaba activo en esas fechas
        result.Should().Be(0);
        _senderMock.Verify(s => s.Send(It.IsAny<ProcessRegularAttendanceCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Employee_IsActiveOn_EvaluatesHireDateAndTerminationDateCorrectly()
    {
        // Arrange (H08 Domain logic)
        var emp = Employee.Create(
            id: EmployeeId.From("EMP-BAJA-2"),
            firstName: "Validacion",
            lastName: "Fechas",
            email: "fechas@test.com",
            phoneNumber: null,
            hireDate: new DateTime(2026, 3, 1),
            gender: Gender.Female,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: ShiftType.Matutino,
            scheduleId: _shift.Id);

        emp.Terminate(new DateTime(2026, 8, 31));

        // Act & Assert
        // Antes de contratación: Inactivo
        emp.IsActiveOn(new DateTime(2026, 2, 28)).Should().BeFalse();

        // Durante período contratado: Activo
        emp.IsActiveOn(new DateTime(2026, 3, 1)).Should().BeTrue();
        emp.IsActiveOn(new DateTime(2026, 6, 15)).Should().BeTrue();
        emp.IsActiveOn(new DateTime(2026, 8, 31)).Should().BeTrue(); // Día de terminación inclusive

        // Después de terminación: Inactivo
        emp.IsActiveOn(new DateTime(2026, 9, 1)).Should().BeFalse();
        emp.IsActiveOn(new DateTime(2026, 12, 31)).Should().BeFalse();
    }

    [Fact]
    public void Employee_Terminate_SetsStatusAndTerminationDate()
    {
        // Arrange
        var emp = Employee.Create(
            id: EmployeeId.From("EMP-BAJA-3"),
            firstName: "Status",
            lastName: "Test",
            email: null,
            phoneNumber: null,
            hireDate: new DateTime(2025, 1, 1),
            gender: Gender.Male,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: ShiftType.Matutino,
            scheduleId: _shift.Id);

        // Act
        emp.Terminate(new DateTime(2026, 5, 20));

        // Assert
        emp.Status.Should().Be(EmployeeStatus.Baja);
        emp.TerminationDate.Should().Be(new DateTime(2026, 5, 20));

        // Reactivar
        emp.Activate();
        emp.Status.Should().Be(EmployeeStatus.Alta);
        emp.TerminationDate.Should().BeNull();
    }
}
