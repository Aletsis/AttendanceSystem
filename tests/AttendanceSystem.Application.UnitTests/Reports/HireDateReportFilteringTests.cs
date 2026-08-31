using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;
using AttendanceSystem.Application.Features.Attendance.Queries.GetAbsenteeismAnalysis;
using AttendanceSystem.Application.Features.Attendance.Queries.GetDailyAttendance;
using AttendanceSystem.Application.Features.Attendance.Queries.GetTardinessAnalysis;
using AttendanceSystem.Application.Features.Reports.Queries.GetAdvancedAttendanceReport;
using AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Abstractions;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Reports;

public class HireDateReportFilteringTests
{
    private readonly BranchId _branchId = BranchId.CreateNew();
    private readonly DepartmentId _departmentId = DepartmentId.CreateNew();
    private readonly PositionId _positionId = PositionId.CreateNew();
    private readonly Employee _employee;
    private readonly Shift _shift;

    public HireDateReportFilteringTests()
    {
        _shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, new TimeSpan(8, 0, 0), ShiftType.Matutino);

        _employee = Employee.Create(
            id: EmployeeId.From("EMP-001"),
            firstName: "Juan",
            lastName: "Perez",
            email: "juan.perez@empresa.com",
            phoneNumber: "555-1234",
            hireDate: new DateTime(2026, 8, 15), // Hire date: Aug 15, 2026
            gender: Gender.Male,
            branchId: _branchId,
            departmentId: _departmentId,
            positionId: _positionId,
            shiftType: ShiftType.Matutino,
            scheduleId: _shift.Id);
    }

    [Fact]
    public async Task ProcessDailyAttendance_WhenDatesArePriorToHireDate_ShouldSkipProcessing()
    {
        // Arrange
        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var attendanceRepoMock = new Mock<IAttendanceRepository>();
        var employeeRepoMock = new Mock<IEmployeeRepository>();
        var shiftRepoMock = new Mock<IShiftRepository>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var senderMock = new Mock<ISender>();
        var loggerMock = new Mock<ILogger<ProcessDailyAttendanceCommandHandler>>();

        employeeRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Employee> { _employee });

        shiftRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Shift> { _shift });

        dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance>());

        attendanceRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord>());

        var handler = new ProcessDailyAttendanceCommandHandler(
            dailyRepoMock.Object,
            attendanceRepoMock.Object,
            employeeRepoMock.Object,
            shiftRepoMock.Object,
            unitOfWorkMock.Object,
            senderMock.Object,
            loggerMock.Object);

        // Process from Aug 10 to Aug 20 (Hire date is Aug 15) -> 11 days total, but only 6 days (15 to 20) should be processed
        var command = new ProcessDailyAttendanceCommand(new DateTime(2026, 8, 10), new DateTime(2026, 8, 20));

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(6); // Only Aug 15, 16, 17, 18, 19, 20
        senderMock.Verify(s => s.Send(It.IsAny<ProcessRegularAttendanceCommand>(), It.IsAny<CancellationToken>()), Times.Exactly(6));
        senderMock.Verify(s => s.Send(It.Is<ProcessRegularAttendanceCommand>(c => c.Date < new DateTime(2026, 8, 15)), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAttendanceReportQuery_WhenRecordsPriorToHireDateExist_ShouldFilterThemOut()
    {
        // Arrange
        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var employeeRepoMock = new Mock<IEmployeeRepository>();
        var branchRepoMock = new Mock<IBranchRepository>();
        var departmentRepoMock = new Mock<IDepartmentRepository>();
        var positionRepoMock = new Mock<IPositionRepository>();

        var branch = Branch.Create("A01", "Sucursal Centro", "Direccion");
        var dept = Department.Create("Sistemas", "Dept Sistemas");
        var pos = Position.Create("Desarrollador", "Puesto Dev", 1000m);

        employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { _employee });
        branchRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Branch> { branch });
        departmentRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department> { dept });
        positionRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Position> { pos });

        // Create 2 records before HireDate (Aug 13, 14) and 2 on/after HireDate (Aug 15, 16)
        var record1 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 13), _shift, null, null);
        var record2 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 14), _shift, null, null);
        var record3 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 15), _shift, new DateTime(2026, 8, 15, 8, 0, 0), new DateTime(2026, 8, 15, 16, 0, 0));
        var record4 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 16), _shift, new DateTime(2026, 8, 16, 8, 0, 0), new DateTime(2026, 8, 16, 16, 0, 0));

        dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { record1, record2, record3, record4 });

        var handler = new GetAttendanceReportQueryHandler(
            dailyRepoMock.Object,
            employeeRepoMock.Object,
            branchRepoMock.Object,
            departmentRepoMock.Object,
            positionRepoMock.Object);

        var query = new GetAttendanceReportQuery(new DateTime(2026, 8, 10), new DateTime(2026, 8, 20));

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.Date >= new DateTime(2026, 8, 15));
    }

    [Fact]
    public async Task GetAdvancedAttendanceReportQuery_WhenAbsencesPriorToHireDateExist_ShouldFilterThemOut()
    {
        // Arrange
        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var employeeRepoMock = new Mock<IEmployeeRepository>();
        var departmentRepoMock = new Mock<IDepartmentRepository>();
        var positionRepoMock = new Mock<IPositionRepository>();
        var branchRepoMock = new Mock<IBranchRepository>();

        employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { _employee });
        departmentRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department>());
        positionRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Position>());
        branchRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Branch>());

        // 1 absence before HireDate (Aug 10) and 1 absence after HireDate (Aug 18)
        var record1 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 10), _shift, null, null);
        var record2 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 18), _shift, null, null);

        dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { record1, record2 });

        var handler = new GetAdvancedAttendanceReportQueryHandler(
            dailyRepoMock.Object,
            employeeRepoMock.Object,
            departmentRepoMock.Object,
            positionRepoMock.Object,
            branchRepoMock.Object);

        var query = new GetAdvancedAttendanceReportQuery(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), "Faltas");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        var summary = result.First();
        summary.Count.Should().Be(1);
        summary.Details.Should().HaveCount(1);
        summary.Details.First().Date.Should().Be(new DateTime(2026, 8, 18));
    }

    [Fact]
    public async Task GetAbsenteeismAnalysisQuery_WhenRecordsPriorToHireDateExist_ShouldNotCountThem()
    {
        // Arrange
        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var employeeRepoMock = new Mock<IEmployeeRepository>();
        var departmentRepoMock = new Mock<IDepartmentRepository>();
        var branchRepoMock = new Mock<IBranchRepository>();

        employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { _employee });
        departmentRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department>());
        branchRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Branch>());

        // 3 days of absence before HireDate (Aug 10, 11, 12), 1 day absence after HireDate (Aug 16), 1 day attendance after HireDate (Aug 17)
        var record1 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 10), _shift, null, null);
        var record2 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 11), _shift, null, null);
        var record3 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 12), _shift, null, null);
        var record4 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 16), _shift, null, null);
        var record5 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 17), _shift, new DateTime(2026, 8, 17, 8, 0, 0), new DateTime(2026, 8, 17, 16, 0, 0));

        dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { record1, record2, record3, record4, record5 });

        var handler = new GetAbsenteeismAnalysisQueryHandler(
            dailyRepoMock.Object,
            employeeRepoMock.Object,
            departmentRepoMock.Object,
            branchRepoMock.Object);

        var query = new GetAbsenteeismAnalysisQuery(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalAbsences.Should().Be(1); // Only Aug 16, ignoring Aug 10, 11, 12
        result.Value.TotalPossibleWorkDays.Should().Be(2); // Aug 16, 17
        result.Value.AbsenteeismRate.Should().Be(50.0);
    }

    [Fact]
    public async Task GetDailyAttendanceByDateRangeQuery_WhenRecordsPriorToHireDateExist_ShouldFilterThemOut()
    {
        // Arrange
        var dailyRepoMock = new Mock<IDailyAttendanceRepository>();
        var employeeRepoMock = new Mock<IEmployeeRepository>();

        employeeRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { _employee });

        var record1 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 10), _shift, null, null);
        var record2 = DailyAttendance.Create(_employee.Id, new DateTime(2026, 8, 15), _shift, new DateTime(2026, 8, 15, 8, 0, 0), new DateTime(2026, 8, 15, 16, 0, 0));

        dailyRepoMock
            .Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { record1, record2 });

        var handler = new GetDailyAttendanceByDateRangeQueryHandler(dailyRepoMock.Object, employeeRepoMock.Object);
        var query = new GetDailyAttendanceByDateRangeQuery(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result.First().Date.Should().Be(new DateTime(2026, 8, 15));
    }
}
