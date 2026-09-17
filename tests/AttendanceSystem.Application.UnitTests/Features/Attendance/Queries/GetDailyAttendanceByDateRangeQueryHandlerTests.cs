using AttendanceSystem.Application.Features.Attendance.Queries.GetDailyAttendance;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Queries;

public class GetDailyAttendanceByDateRangeQueryHandlerTests
{
    private readonly Mock<IDailyAttendanceRepository> _dailyRepoMock;
    private readonly Mock<IEmployeeRepository> _employeeRepoMock;
    private readonly GetDailyAttendanceByDateRangeQueryHandler _handler;

    public GetDailyAttendanceByDateRangeQueryHandlerTests()
    {
        _dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        _employeeRepoMock = new Mock<IEmployeeRepository>();

        _handler = new GetDailyAttendanceByDateRangeQueryHandler(
            _dailyRepoMock.Object,
            _employeeRepoMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnOnlyRecordsEqualOrAfterEmployeeHireDate()
    {
        // Arrange
        var employee = Employee.Create(
            id: EmployeeId.From("EMP-005"),
            firstName: "Hugo",
            lastName: "Sanchez",
            email: "hugo@empresa.com",
            phoneNumber: null,
            hireDate: new DateTime(2026, 9, 10), // Ingresó el 10 de Sep
            gender: Gender.Male,
            branchId: BranchId.CreateNew(),
            departmentId: DepartmentId.CreateNew(),
            positionId: PositionId.CreateNew(),
            shiftType: null);

        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);

        // Record before hire date (Sept 5)
        var daOld = DailyAttendance.Create(employee.Id, new DateTime(2026, 9, 5), shift, null, null, false);
        // Record after hire date (Sept 12)
        var daValid = DailyAttendance.Create(employee.Id, new DateTime(2026, 9, 12), shift, null, null, false);

        _dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { daOld, daValid });

        _employeeRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { employee });

        var query = new GetDailyAttendanceByDateRangeQuery(new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result.First().Date.Should().Be(new DateTime(2026, 9, 12));
    }
}
