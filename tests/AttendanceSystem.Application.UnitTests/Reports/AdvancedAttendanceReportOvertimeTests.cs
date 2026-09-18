using AttendanceSystem.Application.Features.Reports.Queries.GetAdvancedAttendanceReport;
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

namespace AttendanceSystem.Application.UnitTests.Reports;

public class AdvancedAttendanceReportOvertimeTests
{
    private readonly BranchId _branchId = BranchId.CreateNew();
    private readonly DepartmentId _departmentId = DepartmentId.CreateNew();
    private readonly PositionId _positionId = PositionId.CreateNew();
    private readonly Shift _shift;

    public AdvancedAttendanceReportOvertimeTests()
    {
        _shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);
    }

    [Fact]
    public async Task GetAdvancedAttendanceReport_WhenEmployeeWorksNormalScheduleOnRestDay_ShouldHaveZeroOvertime()
    {
        // Arrange
        var employee = Employee.Create(
            id: EmployeeId.From("EMP-001"),
            firstName: "Carlos",
            lastName: "Gomez",
            email: "carlos.gomez@empresa.com",
            phoneNumber: "555-1111",
            hireDate: new DateTime(2026, 1, 1),
            gender: Gender.Male,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: ShiftType.Matutino,
            scheduleId: _shift.Id,
            restDay: WeekDay.Domingo,
            overtimeAuthorized: true);

        var date = new DateTime(2026, 8, 30); // Sunday
        var checkIn = date.AddHours(8);
        var checkOut = date.AddHours(16); // 8 hours worked, exactly standard shift

        var dailyAttendance = DailyAttendance.Create(
            employee.Id,
            date,
            _shift,
            checkIn,
            checkOut,
            isRestDay: true,
            overtimeAuthorized: true);

        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var employeeRepoMock = new Mock<IEmployeeRepository>();
        var departmentRepoMock = new Mock<IDepartmentRepository>();
        var positionRepoMock = new Mock<IPositionRepository>();
        var branchRepoMock = new Mock<IBranchRepository>();

        dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { dailyAttendance });

        employeeRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { employee });

        departmentRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department>());
        positionRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Position>());
        branchRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Branch>());

        var handler = new GetAdvancedAttendanceReportQueryHandler(
            dailyRepoMock.Object,
            employeeRepoMock.Object,
            departmentRepoMock.Object,
            positionRepoMock.Object,
            branchRepoMock.Object);

        var query = new GetAdvancedAttendanceReportQuery(date, date, "HorasExtra");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        var summary = result.First();
        summary.Details.Should().HaveCount(1);
        var detail = summary.Details.First();
        detail.OvertimeMinutes.Should().Be(0);
        detail.WorkedOnRestDay.Should().BeTrue();
        detail.IsRestDay.Should().BeTrue();
    }

    [Fact]
    public async Task GetAdvancedAttendanceReport_WhenEmployeeWorksOvertimeOnRestDay_ShouldCalculateOnlyExcess()
    {
        // Arrange
        var employee = Employee.Create(
            id: EmployeeId.From("EMP-002"),
            firstName: "Maria",
            lastName: "Lopez",
            email: "maria.lopez@empresa.com",
            phoneNumber: "555-2222",
            hireDate: new DateTime(2026, 1, 1),
            gender: Gender.Female,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: ShiftType.Matutino,
            scheduleId: _shift.Id,
            restDay: WeekDay.Domingo,
            overtimeAuthorized: true);

        var date = new DateTime(2026, 8, 30); // Sunday
        var checkIn = date.AddHours(8);
        var checkOut = date.AddHours(18); // 10 hours worked, 8h shift -> 2h (120m) excess

        var dailyAttendance = DailyAttendance.Create(
            employee.Id,
            date,
            _shift,
            checkIn,
            checkOut,
            isRestDay: true,
            overtimeAuthorized: true);

        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var employeeRepoMock = new Mock<IEmployeeRepository>();
        var departmentRepoMock = new Mock<IDepartmentRepository>();
        var positionRepoMock = new Mock<IPositionRepository>();
        var branchRepoMock = new Mock<IBranchRepository>();

        dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { dailyAttendance });

        employeeRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { employee });

        departmentRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department>());
        positionRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Position>());
        branchRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Branch>());

        var handler = new GetAdvancedAttendanceReportQueryHandler(
            dailyRepoMock.Object,
            employeeRepoMock.Object,
            departmentRepoMock.Object,
            positionRepoMock.Object,
            branchRepoMock.Object);

        var query = new GetAdvancedAttendanceReportQuery(date, date, "HorasExtra");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        var summary = result.First();
        summary.Details.Should().HaveCount(1);
        var detail = summary.Details.First();
        detail.OvertimeMinutes.Should().Be(120);
        detail.WorkedOnRestDay.Should().BeTrue();
    }

    [Fact]
    public async Task GetAdvancedAttendanceReport_WhenOvertimeNotAuthorized_ShouldReturnZeroOvertimeEvenIfPunchedExcess()
    {
        // Arrange
        var employee = Employee.Create(
            id: EmployeeId.From("EMP-003"),
            firstName: "Pedro",
            lastName: "Ramirez",
            email: "pedro.ramirez@empresa.com",
            phoneNumber: "555-3333",
            hireDate: new DateTime(2026, 1, 1),
            gender: Gender.Male,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: ShiftType.Matutino,
            scheduleId: _shift.Id,
            restDay: WeekDay.Domingo,
            overtimeAuthorized: false);

        var date = new DateTime(2026, 8, 30); // Sunday
        var checkIn = date.AddHours(8);
        var checkOut = date.AddHours(18); // 10 hours worked

        var dailyAttendance = DailyAttendance.Create(
            employee.Id,
            date,
            _shift,
            checkIn,
            checkOut,
            isRestDay: true,
            overtimeAuthorized: false);

        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var employeeRepoMock = new Mock<IEmployeeRepository>();
        var departmentRepoMock = new Mock<IDepartmentRepository>();
        var positionRepoMock = new Mock<IPositionRepository>();
        var branchRepoMock = new Mock<IBranchRepository>();

        dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { dailyAttendance });

        employeeRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { employee });

        departmentRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department>());
        positionRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Position>());
        branchRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Branch>());

        var handler = new GetAdvancedAttendanceReportQueryHandler(
            dailyRepoMock.Object,
            employeeRepoMock.Object,
            departmentRepoMock.Object,
            positionRepoMock.Object,
            branchRepoMock.Object);

        var query = new GetAdvancedAttendanceReportQuery(date, date, "HorasExtra");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        var summary = result.First();
        summary.Details.Should().HaveCount(1);
        var detail = summary.Details.First();
        detail.OvertimeMinutes.Should().Be(0);
        summary.TotalMetric.Should().Be(0);
    }
}
