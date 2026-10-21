using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Employees.Commands;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Employees;

public class UpdateEmployeeCommandHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepoMock;
    private readonly Mock<IBranchRepository> _branchRepoMock;
    private readonly Mock<IDepartmentRepository> _departmentRepoMock;
    private readonly Mock<IPositionRepository> _positionRepoMock;
    private readonly Mock<IShiftRepository> _shiftRepoMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAttendanceConsolidationRepository> _consolidationMock = new();
    private readonly Mock<IConsolidationWakeup> _wakeupMock = new();
    private readonly Mock<ILogger<UpdateEmployeeCommandHandler>> _loggerMock;
    private readonly UpdateEmployeeCommandHandler _handler;

    private readonly Branch _sampleBranch;
    private readonly Department _sampleDepartment;
    private readonly Position _samplePosition;
    private readonly Shift _sampleShift;
    private readonly Employee _sampleEmployee;

    public UpdateEmployeeCommandHandlerTests()
    {
        _employeeRepoMock = new Mock<IEmployeeRepository>();
        _branchRepoMock = new Mock<IBranchRepository>();
        _departmentRepoMock = new Mock<IDepartmentRepository>();
        _positionRepoMock = new Mock<IPositionRepository>();
        _shiftRepoMock = new Mock<IShiftRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _loggerMock = new Mock<ILogger<UpdateEmployeeCommandHandler>>();

        _handler = new UpdateEmployeeCommandHandler(
            _employeeRepoMock.Object,
            _branchRepoMock.Object,
            _departmentRepoMock.Object,
            _positionRepoMock.Object,
            _shiftRepoMock.Object,
            _unitOfWorkMock.Object,
            Mock.Of<IAttendanceCalculationLockService>(),
            _consolidationMock.Object,
            _wakeupMock.Object,
            _loggerMock.Object);

        _sampleBranch = Branch.Create("A01", "Sucursal Matriz", "Av. Principal 123");
        _sampleDepartment = Department.Create("Sistemas", "Departamento TI");
        _samplePosition = Position.Create("Desarrollador", "Senior Dev", 25000m);
        _sampleDepartment.AddPosition(_samplePosition);
        _sampleShift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);

        _sampleEmployee = Employee.Create(
            id: EmployeeId.From("EMP-001"),
            firstName: "Pedro",
            lastName: "Perez",
            email: "pedro@empresa.com",
            phoneNumber: "555-1111",
            hireDate: new DateTime(2025, 1, 1),
            gender: Gender.Male,
            branchId: _sampleBranch.Id,
            departmentId: _sampleDepartment.Id,
            positionId: _samplePosition.Id,
            shiftType: ShiftType.Matutino);
    }

    [Fact]
    public async Task Handle_WhenEmployeeNotFound_ShouldReturnFailure()
    {
        // Arrange
        var command = new UpdateEmployeeCommand(
            Id: "NON-EXISTENT",
            FirstName: "Test",
            LastName: "Test",
            Email: "test@empresa.com",
            PhoneNumber: null,
            HireDate: DateTime.Today,
            Gender: Gender.Male,
            Status: EmployeeStatus.Alta,
            BranchId: _sampleBranch.Id.Value.ToString(),
            DepartmentId: _sampleDepartment.Id.Value.ToString(),
            PositionId: _samplePosition.Id.Value.ToString(),
            ShiftType: ShiftType.Matutino,
            ScheduleId: null,
            RestDay: 0,
            OvertimeAuthorized: false,
            OvertimeCalculationMethod: OvertimeCalculationMethod.NoRounding,
            OvertimeCapType: OvertimeCapType.Daily,
            OvertimeCapMinutes: null,
            CalculateOvertimeBeforeEntry: false);

        _employeeRepoMock
            .Setup(r => r.GetByIdAsync(EmployeeId.From("NON-EXISTENT"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("No existe el empleado");
    }

    [Fact]
    public async Task Handle_WhenPositionDoesNotBelongToDepartment_ShouldReturnFailure()
    {
        // Arrange
        var anotherDepartment = Department.Create("Recursos Humanos", "RH");
        var command = new UpdateEmployeeCommand(
            Id: "EMP-001",
            FirstName: "Pedro",
            LastName: "Perez",
            Email: "pedro@empresa.com",
            PhoneNumber: null,
            HireDate: DateTime.Today,
            Gender: Gender.Male,
            Status: EmployeeStatus.Alta,
            BranchId: _sampleBranch.Id.Value.ToString(),
            DepartmentId: anotherDepartment.Id.Value.ToString(),
            PositionId: _samplePosition.Id.Value.ToString(),
            ShiftType: ShiftType.Matutino,
            ScheduleId: null,
            RestDay: 0,
            OvertimeAuthorized: false,
            OvertimeCalculationMethod: OvertimeCalculationMethod.NoRounding,
            OvertimeCapType: OvertimeCapType.Daily,
            OvertimeCapMinutes: null,
            CalculateOvertimeBeforeEntry: false);

        _employeeRepoMock
            .Setup(r => r.GetByIdAsync(_sampleEmployee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleEmployee);

        _branchRepoMock
            .Setup(r => r.GetByIdAsync(_sampleBranch.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleBranch);

        _departmentRepoMock
            .Setup(r => r.GetByIdAsync(anotherDepartment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(anotherDepartment);

        _positionRepoMock
            .Setup(r => r.GetByIdAsync(_samplePosition.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_samplePosition);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("no pertenece al departamento");
    }

    [Fact]
    public async Task Handle_WhenScheduleDoesNotBelongToShiftType_ShouldReturnFailure()
    {
        // Arrange
        var command = new UpdateEmployeeCommand(
            Id: "EMP-001",
            FirstName: "Pedro",
            LastName: "Perez",
            Email: "pedro@empresa.com",
            PhoneNumber: null,
            HireDate: DateTime.Today,
            Gender: Gender.Male,
            Status: EmployeeStatus.Alta,
            BranchId: _sampleBranch.Id.Value.ToString(),
            DepartmentId: _sampleDepartment.Id.Value.ToString(),
            PositionId: _samplePosition.Id.Value.ToString(),
            ShiftType: ShiftType.Nocturno, // Different from _sampleShift.ShiftType (Matutino)
            ScheduleId: _sampleShift.Id.Value.ToString(),
            RestDay: 0,
            OvertimeAuthorized: false,
            OvertimeCalculationMethod: OvertimeCalculationMethod.NoRounding,
            OvertimeCapType: OvertimeCapType.Daily,
            OvertimeCapMinutes: null,
            CalculateOvertimeBeforeEntry: false);

        _employeeRepoMock
            .Setup(r => r.GetByIdAsync(_sampleEmployee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleEmployee);

        _branchRepoMock
            .Setup(r => r.GetByIdAsync(_sampleBranch.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleBranch);

        _departmentRepoMock
            .Setup(r => r.GetByIdAsync(_sampleDepartment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleDepartment);

        _positionRepoMock
            .Setup(r => r.GetByIdAsync(_samplePosition.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_samplePosition);

        _shiftRepoMock
            .Setup(r => r.GetByIdAsync(_sampleShift.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleShift);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("no corresponde al turno seleccionado");
    }

    [Fact]
    public async Task Handle_WhenValidParameters_ShouldUpdateEmployeeAndReturnDto()
    {
        // Arrange
        var command = new UpdateEmployeeCommand(
            Id: "EMP-001",
            FirstName: "Pedro Modificado",
            LastName: "Perez Lopez",
            Email: "pedro.mod@empresa.com",
            PhoneNumber: "555-9999",
            HireDate: new DateTime(2025, 1, 1),
            Gender: Gender.Male,
            Status: EmployeeStatus.Alta,
            BranchId: _sampleBranch.Id.Value.ToString(),
            DepartmentId: _sampleDepartment.Id.Value.ToString(),
            PositionId: _samplePosition.Id.Value.ToString(),
            ShiftType: ShiftType.Matutino,
            ScheduleId: _sampleShift.Id.Value.ToString(),
            RestDay: 0,
            OvertimeAuthorized: true,
            OvertimeCalculationMethod: OvertimeCalculationMethod.NoRounding,
            OvertimeCapType: OvertimeCapType.Daily,
            OvertimeCapMinutes: 60,
            CalculateOvertimeBeforeEntry: false);

        _employeeRepoMock
            .Setup(r => r.GetByIdAsync(_sampleEmployee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleEmployee);

        _branchRepoMock
            .Setup(r => r.GetByIdAsync(_sampleBranch.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleBranch);

        _departmentRepoMock
            .Setup(r => r.GetByIdAsync(_sampleDepartment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleDepartment);

        _positionRepoMock
            .Setup(r => r.GetByIdAsync(_samplePosition.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_samplePosition);

        _shiftRepoMock
            .Setup(r => r.GetByIdAsync(_sampleShift.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleShift);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.FirstName.Should().Be("Pedro Modificado");
        result.Value.LastName.Should().Be("Perez Lopez");
        result.Value.Email.Should().Be("pedro.mod@empresa.com");
        _employeeRepoMock.Verify(r => r.Update(_sampleEmployee), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #region Vigencia de cambios (F06-08)

    private UpdateEmployeeCommand ValidCommand(DateTime? effectiveFrom = null, EmployeeStatus status = EmployeeStatus.Alta, string? scheduleId = null) =>
        new(
            Id: "EMP-001",
            FirstName: "Pedro",
            LastName: "Perez",
            Email: "pedro@empresa.com",
            PhoneNumber: null,
            HireDate: new DateTime(2025, 1, 1),
            Gender: Gender.Male,
            Status: status,
            BranchId: _sampleBranch.Id.Value.ToString(),
            DepartmentId: _sampleDepartment.Id.Value.ToString(),
            PositionId: _samplePosition.Id.Value.ToString(),
            ShiftType: ShiftType.Matutino,
            ScheduleId: scheduleId,
            RestDay: null,
            OvertimeAuthorized: false,
            OvertimeCalculationMethod: OvertimeCalculationMethod.NoRounding,
            OvertimeCapType: OvertimeCapType.None,
            OvertimeCapMinutes: null,
            CalculateOvertimeBeforeEntry: false,
            EffectiveFrom: effectiveFrom);

    private void SetupLookups()
    {
        _employeeRepoMock.Setup(r => r.GetByIdAsync(_sampleEmployee.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_sampleEmployee);
        _branchRepoMock.Setup(r => r.GetByIdAsync(_sampleBranch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_sampleBranch);
        _departmentRepoMock.Setup(r => r.GetByIdAsync(_sampleDepartment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_sampleDepartment);
        _positionRepoMock.Setup(r => r.GetByIdAsync(_samplePosition.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_samplePosition);
        _shiftRepoMock.Setup(r => r.GetByIdAsync(_sampleShift.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_sampleShift);
    }

    [Fact]
    public async Task Handle_ScheduleChangeWithPastEffectiveDate_RequestsDurableRecalculationFromThatDate()
    {
        SetupLookups();
        var from = DateTime.Today.AddDays(-8);

        var result = await _handler.Handle(ValidCommand(from, scheduleId: _sampleShift.Id.Value.ToString()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsSuccess ? string.Empty : result.Error);
        _sampleEmployee.AssignmentOn(from.AddDays(-1)).ScheduleId.Should().BeNull();
        _sampleEmployee.AssignmentOn(from).ScheduleId.Should().Be(_sampleShift.Id);
        _consolidationMock.Verify(c => c.RequestAsync(_sampleEmployee.Id, from, DateTime.Today, It.IsAny<CancellationToken>()), Times.Once);
        _wakeupMock.Verify(w => w.Signal(), Times.Once);
    }

    [Fact]
    public async Task Handle_FutureScheduleChange_IsRecordedWithoutRecalculation()
    {
        SetupLookups();
        var from = DateTime.Today.AddDays(6);

        var result = await _handler.Handle(ValidCommand(from, scheduleId: _sampleShift.Id.Value.ToString()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _sampleEmployee.AssignmentOn(DateTime.Today).ScheduleId.Should().BeNull("el horario nuevo aún no rige");
        _sampleEmployee.AssignmentOn(from).ScheduleId.Should().Be(_sampleShift.Id);
        _consolidationMock.Verify(c => c.RequestAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RetroactiveTermination_ClosesThePeriodOnTheLastWorkedDay()
    {
        SetupLookups();
        var lastWorked = DateTime.Today.AddDays(-15);

        var result = await _handler.Handle(ValidCommand(lastWorked, EmployeeStatus.Baja), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _sampleEmployee.IsActiveOn(lastWorked).Should().BeTrue();
        _sampleEmployee.IsActiveOn(lastWorked.AddDays(1)).Should().BeFalse("después de la baja no hay faltas");
        _consolidationMock.Verify(c => c.RequestAsync(_sampleEmployee.Id, lastWorked, DateTime.Today, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RehireOnOrBeforeTheLastTermination_IsRejected()
    {
        SetupLookups();
        _sampleEmployee.Terminate(DateTime.Today.AddDays(-10));

        var result = await _handler.Handle(ValidCommand(DateTime.Today.AddDays(-12)), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("posterior a la última baja");
        _sampleEmployee.Status.Should().Be(EmployeeStatus.Baja);
        _consolidationMock.Verify(c => c.RequestAsync(It.IsAny<EmployeeId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_FutureTermination_IsRejected()
    {
        SetupLookups();

        var result = await _handler.Handle(ValidCommand(DateTime.Today.AddDays(3), EmployeeStatus.Baja), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("fecha futura");
    }

    #endregion
}
