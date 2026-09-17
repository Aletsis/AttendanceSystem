using AttendanceSystem.Application.Features.Attendance.Queries.GetAbsenteeismAnalysis;
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

public class GetAbsenteeismAnalysisQueryHandlerTests
{
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock;
    private readonly Mock<IEmployeeRepository> _employeeRepoMock;
    private readonly Mock<IDepartmentRepository> _deptRepoMock;
    private readonly Mock<IBranchRepository> _branchRepoMock;
    private readonly GetAbsenteeismAnalysisQueryHandler _handler;

    private readonly Branch _sampleBranch;
    private readonly Department _sampleDepartment;
    private readonly Position _samplePosition;
    private readonly Shift _sampleShift;
    private readonly Employee _sampleEmployee;

    public GetAbsenteeismAnalysisQueryHandlerTests()
    {
        _dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        _employeeRepoMock = new Mock<IEmployeeRepository>();
        _deptRepoMock = new Mock<IDepartmentRepository>();
        _branchRepoMock = new Mock<IBranchRepository>();

        _handler = new GetAbsenteeismAnalysisQueryHandler(
            _dailyRepoMock.Object,
            _employeeRepoMock.Object,
            _deptRepoMock.Object,
            _branchRepoMock.Object);

        _sampleBranch = Branch.Create("A01", "Sucursal Norte", null);
        _sampleDepartment = Department.Create("Produccion", null);
        _samplePosition = Position.Create("Operador", null, 10000m);
        _sampleShift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);

        _sampleEmployee = Employee.Create(
            id: EmployeeId.From("EMP-001"),
            firstName: "Roberto",
            lastName: "Juarez",
            email: "roberto@empresa.com",
            phoneNumber: null,
            hireDate: new DateTime(2025, 1, 1),
            gender: Gender.Male,
            branchId: _sampleBranch.Id,
            departmentId: _sampleDepartment.Id,
            positionId: _samplePosition.Id,
            shiftType: ShiftType.Matutino);
    }

    [Fact]
    public async Task Handle_WhenNoRecords_ShouldReturnEmptyAnalysisResult()
    {
        // Arrange
        _dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance>());

        _employeeRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { _sampleEmployee });

        _deptRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department> { _sampleDepartment });

        _branchRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Branch> { _sampleBranch });

        var query = new GetAbsenteeismAnalysisQuery(DateTime.Today.AddDays(-7), DateTime.Today);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.TotalAbsences.Should().Be(0);
        result.Value.AbsenteeismRate.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenAbsencesExist_ShouldCalculateRatesAndRankings()
    {
        // Arrange
        var date1 = new DateTime(2026, 9, 14); // Lunes
        var date2 = new DateTime(2026, 9, 15); // Martes

        // Day 1: Absent
        var da1 = DailyAttendance.Create(_sampleEmployee.Id, date1, _sampleShift, null, null, isRestDay: false);
        // Day 2: Present
        var da2 = DailyAttendance.Create(_sampleEmployee.Id, date2, _sampleShift, date2.AddHours(8), date2.AddHours(16), isRestDay: false);

        var dailyRecords = new List<DailyAttendance> { da1, da2 };

        _dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dailyRecords);

        _employeeRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { _sampleEmployee });

        _deptRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Department> { _sampleDepartment });

        _branchRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Branch> { _sampleBranch });

        var query = new GetAbsenteeismAnalysisQuery(date1, date2);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.TotalAbsences.Should().Be(1);
        result.Value.TotalPossibleWorkDays.Should().Be(2);
        result.Value.AbsenteeismRate.Should().Be(50.0); // 1 de 2 días = 50%
        result.Value.TopAbsentEmployees.Should().HaveCount(1);
        result.Value.TopAbsentEmployees.First().EmployeeName.Should().Be("Roberto Juarez");
        result.Value.AbsencesByDepartment.Should().HaveCount(1);
        result.Value.AbsencesByDepartment.First().DepartmentName.Should().Be("Produccion");
    }
}
