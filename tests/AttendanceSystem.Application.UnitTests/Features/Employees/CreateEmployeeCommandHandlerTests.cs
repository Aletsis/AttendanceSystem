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

public class CreateEmployeeCommandHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepoMock;
    private readonly Mock<IBranchRepository> _branchRepoMock;
    private readonly Mock<IDepartmentRepository> _departmentRepoMock;
    private readonly Mock<IPositionRepository> _positionRepoMock;
    private readonly Mock<IShiftRepository> _shiftRepoMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ILogger<CreateEmployeeCommandHandler>> _loggerMock;
    private readonly CreateEmployeeCommandHandler _handler;

    private readonly Branch _sampleBranch;
    private readonly Department _sampleDepartment;
    private readonly Position _samplePosition;
    private readonly Shift _sampleShift;

    public CreateEmployeeCommandHandlerTests()
    {
        _employeeRepoMock = new Mock<IEmployeeRepository>();
        _branchRepoMock = new Mock<IBranchRepository>();
        _departmentRepoMock = new Mock<IDepartmentRepository>();
        _positionRepoMock = new Mock<IPositionRepository>();
        _shiftRepoMock = new Mock<IShiftRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _loggerMock = new Mock<ILogger<CreateEmployeeCommandHandler>>();

        _handler = new CreateEmployeeCommandHandler(
            _employeeRepoMock.Object,
            _branchRepoMock.Object,
            _departmentRepoMock.Object,
            _positionRepoMock.Object,
            _shiftRepoMock.Object,
            _unitOfWorkMock.Object,
            _loggerMock.Object);

        _sampleBranch = Branch.Create("A01", "Sucursal Matriz", "Av. Principal 123");
        _sampleDepartment = Department.Create("Sistemas", "Departamento TI");
        _samplePosition = Position.Create("Desarrollador", "Senior Dev", 25000m);
        _sampleDepartment.AddPosition(_samplePosition);
        _sampleShift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
    }

    [Fact]
    public async Task Handle_WhenEmployeeAlreadyExists_ShouldReturnFailure()
    {
        // Arrange
        var command = new CreateEmployeeCommand(
            Id: "EMP-001",
            FirstName: "Juan",
            LastName: "Perez",
            Email: "juan@empresa.com",
            PhoneNumber: "555-1234",
            HireDate: new DateTime(2025, 1, 1),
            Gender: Gender.Male,
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
            .Setup(r => r.ExistsAsync(EmployeeId.From("EMP-001"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Ya existe un empleado con el ID EMP-001");
        _employeeRepoMock.Verify(r => r.Add(It.IsAny<Employee>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ShouldReturnFailure()
    {
        // Arrange
        var command = new CreateEmployeeCommand(
            Id: "EMP-002",
            FirstName: "Ana",
            LastName: "Lopez",
            Email: "ana@empresa.com",
            PhoneNumber: null,
            HireDate: new DateTime(2025, 1, 1),
            Gender: Gender.Female,
            BranchId: Guid.NewGuid().ToString(),
            DepartmentId: _sampleDepartment.Id.Value.ToString(),
            PositionId: _samplePosition.Id.Value.ToString(),
            ShiftType: null,
            ScheduleId: null,
            RestDay: 0,
            OvertimeAuthorized: false,
            OvertimeCalculationMethod: OvertimeCalculationMethod.NoRounding,
            OvertimeCapType: OvertimeCapType.Daily,
            OvertimeCapMinutes: null,
            CalculateOvertimeBeforeEntry: false);

        _employeeRepoMock
            .Setup(r => r.ExistsAsync(It.IsAny<EmployeeId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _branchRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<BranchId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Branch?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("No existe la sucursal");
    }

    [Fact]
    public async Task Handle_WhenPositionDoesNotBelongToDepartment_ShouldReturnFailure()
    {
        // Arrange
        var anotherDepartment = Department.Create("Recursos Humanos", "RH");
        var command = new CreateEmployeeCommand(
            Id: "EMP-004",
            FirstName: "Carlos",
            LastName: "Gomez",
            Email: "carlos@empresa.com",
            PhoneNumber: null,
            HireDate: new DateTime(2025, 1, 1),
            Gender: Gender.Male,
            BranchId: _sampleBranch.Id.Value.ToString(),
            DepartmentId: anotherDepartment.Id.Value.ToString(),
            PositionId: _samplePosition.Id.Value.ToString(),
            ShiftType: null,
            ScheduleId: null,
            RestDay: 0,
            OvertimeAuthorized: false,
            OvertimeCalculationMethod: OvertimeCalculationMethod.NoRounding,
            OvertimeCapType: OvertimeCapType.Daily,
            OvertimeCapMinutes: null,
            CalculateOvertimeBeforeEntry: false);

        _employeeRepoMock
            .Setup(r => r.ExistsAsync(It.IsAny<EmployeeId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

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
        var command = new CreateEmployeeCommand(
            Id: "EMP-005",
            FirstName: "Maria",
            LastName: "Diaz",
            Email: "maria@empresa.com",
            PhoneNumber: null,
            HireDate: new DateTime(2025, 1, 1),
            Gender: Gender.Female,
            BranchId: _sampleBranch.Id.Value.ToString(),
            DepartmentId: _sampleDepartment.Id.Value.ToString(),
            PositionId: _samplePosition.Id.Value.ToString(),
            ShiftType: ShiftType.Vespertino, // Different from _sampleShift.ShiftType (Matutino)
            ScheduleId: _sampleShift.Id.Value.ToString(),
            RestDay: 0,
            OvertimeAuthorized: false,
            OvertimeCalculationMethod: OvertimeCalculationMethod.NoRounding,
            OvertimeCapType: OvertimeCapType.Daily,
            OvertimeCapMinutes: null,
            CalculateOvertimeBeforeEntry: false);

        _employeeRepoMock
            .Setup(r => r.ExistsAsync(It.IsAny<EmployeeId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

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
    public async Task Handle_WhenAllDataIsValid_ShouldCreateEmployeeAndReturnDto()
    {
        // Arrange
        var command = new CreateEmployeeCommand(
            Id: "EMP-003",
            FirstName: "Laura",
            LastName: "Torres",
            Email: "laura@empresa.com",
            PhoneNumber: "555-9876",
            HireDate: new DateTime(2025, 2, 1),
            Gender: Gender.Female,
            BranchId: _sampleBranch.Id.Value.ToString(),
            DepartmentId: _sampleDepartment.Id.Value.ToString(),
            PositionId: _samplePosition.Id.Value.ToString(),
            ShiftType: ShiftType.Matutino,
            ScheduleId: _sampleShift.Id.Value.ToString(),
            RestDay: 0,
            OvertimeAuthorized: true,
            OvertimeCalculationMethod: OvertimeCalculationMethod.NoRounding,
            OvertimeCapType: OvertimeCapType.Daily,
            OvertimeCapMinutes: 120,
            CalculateOvertimeBeforeEntry: false);

        _employeeRepoMock
            .Setup(r => r.ExistsAsync(It.IsAny<EmployeeId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

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
        result.Value!.Id.Should().Be("EMP-003");
        result.Value.FirstName.Should().Be("Laura");
        result.Value.LastName.Should().Be("Torres");
        result.Value.Email.Should().Be("laura@empresa.com");
        result.Value.BranchName.Should().Be("Sucursal Matriz");
        result.Value.DepartmentName.Should().Be("Sistemas");
        result.Value.PositionName.Should().Be("Desarrollador");
        result.Value.ScheduleName.Should().Be("Matutino");

        _employeeRepoMock.Verify(r => r.Add(It.Is<Employee>(e => e.Id.Value == "EMP-003")), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
