using AttendanceSystem.Application.Features.Roster.Queries.GetRosterEmployees;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Roster;

public class GetRosterEmployeesQueryHandlerTests
{
    private readonly Mock<IShiftRosterRepository> _rosterRepoMock = new();
    private readonly Mock<IEmployeeRepository> _employeeRepoMock = new();
    private readonly Mock<IBranchRepository> _branchRepoMock = new();
    private readonly Mock<IDepartmentRepository> _departmentRepoMock = new();
    private readonly Mock<IPositionRepository> _positionRepoMock = new();
    private readonly Mock<IShiftRepository> _shiftRepoMock = new();

    private readonly GetRosterEmployeesQueryHandler _handler;

    public GetRosterEmployeesQueryHandlerTests()
    {
        _branchRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Branch>());
        _departmentRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department>());
        _positionRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>());
        _shiftRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Shift>());

        _handler = new GetRosterEmployeesQueryHandler(
            _rosterRepoMock.Object,
            _employeeRepoMock.Object,
            _branchRepoMock.Object,
            _departmentRepoMock.Object,
            _positionRepoMock.Object,
            _shiftRepoMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnOnlyEmployeesWithRotatingSchemeOrRosterRecords()
    {
        // Arrange
        var branchId = BranchId.CreateNew();
        var deptId = DepartmentId.CreateNew();
        var posId = PositionId.CreateNew();

        var empRotativo = Employee.Create(
            EmployeeId.From("EMP-001"),
            "Juan",
            "Perez",
            "juan@test.com",
            null,
            DateTime.Today.AddYears(-1),
            Gender.Male,
            branchId,
            deptId,
            posId,
            ShiftType.Rotativo);

        var empFixedWithRoster = Employee.Create(
            EmployeeId.From("EMP-002"),
            "Maria",
            "Lopez",
            "maria@test.com",
            null,
            DateTime.Today.AddYears(-1),
            Gender.Female,
            branchId,
            deptId,
            posId,
            ShiftType.Matutino);

        var empFixedWithoutRoster = Employee.Create(
            EmployeeId.From("EMP-003"),
            "Carlos",
            "Gomez",
            "carlos@test.com",
            null,
            DateTime.Today.AddYears(-1),
            Gender.Male,
            branchId,
            deptId,
            posId,
            ShiftType.Matutino);

        _employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { empRotativo, empFixedWithRoster, empFixedWithoutRoster });

        var roster = ShiftRoster.Create(
            empFixedWithRoster.Id,
            DateTime.Today,
            null,
            true,
            "Esquema 4x3 (Descanso)");

        _rosterRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster> { roster });

        // Act
        var result = await _handler.Handle(new GetRosterEmployeesQuery(), CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Select(x => x.EmployeeId).Should().Contain("EMP-001");
        result.Select(x => x.EmployeeId).Should().Contain("EMP-002");
        result.Select(x => x.EmployeeId).Should().NotContain("EMP-003");

        var emp2Dto = result.First(x => x.EmployeeId == "EMP-002");
        emp2Dto.SchemeName.Should().Be("Esquema 4x3");
        emp2Dto.HasRoster.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldDetectRotativo2x8SchemeNameFromNotes()
    {
        // Arrange
        var branchId = BranchId.CreateNew();
        var deptId = DepartmentId.CreateNew();
        var posId = PositionId.CreateNew();

        var emp = Employee.Create(
            EmployeeId.From("EMP-010"),
            "Pedro",
            "Ramirez",
            "pedro@test.com",
            null,
            DateTime.Today.AddYears(-1),
            Gender.Male,
            branchId,
            deptId,
            posId,
            ShiftType.Rotativo);

        _employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { emp });

        var roster = ShiftRoster.Create(
            emp.Id,
            DateTime.Today,
            ShiftId.From(Guid.NewGuid()),
            false,
            "Rotativo 2x8 (Turno 1)");

        _rosterRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftRoster> { roster });

        // Act
        var result = await _handler.Handle(new GetRosterEmployeesQuery(), CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].SchemeName.Should().Be("Rotativo 2x8");
    }
}
