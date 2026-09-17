using AttendanceSystem.Application.Features.Attendance.Queries.GetTardinessAnalysis;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Queries;

public class GetTardinessAnalysisQueryHandlerTests
{
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock;
    private readonly Mock<IEmployeeRepository> _employeeRepoMock;
    private readonly Mock<IDepartmentRepository> _deptRepoMock;
    private readonly Mock<IBranchRepository> _branchRepoMock;
    private readonly GetTardinessAnalysisQueryHandler _handler;

    private readonly Branch _sampleBranch;
    private readonly Department _sampleDepartment;
    private readonly Position _samplePosition;
    private readonly Shift _sampleShift;
    private readonly Employee _sampleEmployee;

    public GetTardinessAnalysisQueryHandlerTests()
    {
        _dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        _employeeRepoMock = new Mock<IEmployeeRepository>();
        _deptRepoMock = new Mock<IDepartmentRepository>();
        _branchRepoMock = new Mock<IBranchRepository>();

        _handler = new GetTardinessAnalysisQueryHandler(
            _dailyRepoMock.Object,
            _employeeRepoMock.Object,
            _deptRepoMock.Object,
            _branchRepoMock.Object);

        _sampleBranch = Branch.Create("A01", "Sucursal Norte", null);
        _sampleDepartment = Department.Create("Ventas", null);
        _samplePosition = Position.Create("Ejecutivo", null, 15000m);
        _sampleShift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);

        _sampleEmployee = Employee.Create(
            id: EmployeeId.From("EMP-002"),
            firstName: "Sonia",
            lastName: "Navarro",
            email: "sonia@empresa.com",
            phoneNumber: null,
            hireDate: new DateTime(2025, 1, 1),
            gender: Gender.Female,
            branchId: _sampleBranch.Id,
            departmentId: _sampleDepartment.Id,
            positionId: _samplePosition.Id,
            shiftType: ShiftType.Matutino);
    }

    [Fact]
    public async Task Handle_WhenLateMinutesRecorded_ShouldCalculateTardinessMetrics()
    {
        // Arrange
        var date = new DateTime(2026, 9, 14); // Lunes
        // CheckIn at 8:30 -> 30 min late (exceeds 10 min tolerance)
        var da = DailyAttendance.Create(_sampleEmployee.Id, date, _sampleShift, date.AddHours(8).AddMinutes(30), date.AddHours(16), isRestDay: false);

        _dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { da });

        _employeeRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { _sampleEmployee });

        _deptRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department> { _sampleDepartment });

        _branchRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Branch> { _sampleBranch });

        var query = new GetTardinessAnalysisQuery(date, date);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.TotalTardies.Should().Be(1);
        result.Value.TotalTardinessMinutes.Should().Be(30);
        result.Value.TopTardyEmployees.Should().HaveCount(1);
        result.Value.TopTardyEmployees.First().EmployeeName.Should().Be("Sonia Navarro");
        result.Value.TopTardyEmployees.First().TotalMinutes.Should().Be(30);
    }
}
