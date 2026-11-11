using AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;
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

public class FlexibleReportScheduledCheckInTests
{
    [Fact]
    public async Task AttendanceReport_FlexibleShift_UsesEffectiveScheduledCheckInInCivilTime()
    {
        var flexShift = Shift.Create(
            name: "Flexible",
            startTime: new TimeSpan(8, 0, 0),
            toleranceMinutes: 5,
            workHours: new TimeSpan(8, 0, 0),
            shiftType: ShiftType.Flexible,
            roundingsEnabled: true,
            roundingInterval: 15,
            flexWindowEndTime: new TimeSpan(9, 30, 0),
            hasEntryWindow: true);

        var employee = Employee.Create(
            EmployeeId.From("EMP-FLEX-1"), "Luis", "Flex", null, null,
            new DateTime(2026, 1, 1), Gender.Male,
            BranchId.CreateNew(), DepartmentId.CreateNew(), PositionId.CreateNew(),
            ShiftType.Flexible, flexShift.Id);

        // Sucursal en America/Mexico_City (UTC-6): 08:21 local = 14:21 UTC (supera 5 min del bloque 08:15 -> 08:30)
        var date = new DateTime(2026, 10, 5);
        var checkInUtc = new DateTime(2026, 10, 5, 14, 21, 0, DateTimeKind.Utc);
        var checkOutUtc = new DateTime(2026, 10, 5, 22, 30, 0, DateTimeKind.Utc);
        var day = DailyAttendance.Create(
            employee.Id, date, flexShift, checkInUtc, checkOutUtc,
            isRestDay: false, timeZoneId: "America/Mexico_City");

        var dailyRepo = new Mock<IDailyAttendanceRepository>();
        dailyRepo.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { day });
        var empRepo = new Mock<IEmployeeRepository>();
        empRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { employee });
        var branchRepo = new Mock<IBranchRepository>();
        branchRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Branch>());
        var deptRepo = new Mock<IDepartmentRepository>();
        deptRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department>());
        var posRepo = new Mock<IPositionRepository>();
        posRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Position>());

        var handler = new GetAttendanceReportQueryHandler(
            dailyRepo.Object, empRepo.Object, branchRepo.Object, deptRepo.Object, posRepo.Object,
            new Mock<ISystemConfigurationRepository>().Object);

        var row = (await handler.Handle(new GetAttendanceReportQuery(date, date), CancellationToken.None)).Single();

        row.ScheduledCheckIn.Should().Be(new TimeSpan(8, 30, 0));
        row.ScheduledCheckOut.Should().Be(new TimeSpan(16, 0, 0));
    }
}
