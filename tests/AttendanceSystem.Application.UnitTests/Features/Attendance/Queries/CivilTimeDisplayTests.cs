using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AttendanceSystem.Application.Features.Attendance.Queries.GetAttendanceLogs;
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
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance.Queries;

/// <summary>
/// Las pantallas muestran la hora civil de la sucursal: un marcaje manual de las 08:00 (UTC-6) se guarda como
/// 14:00 UTC y debe verse como 08:00 en la bitácora y en el reporte.
/// </summary>
public class CivilTimeDisplayTests
{
    private const string Zone = "America/Mexico_City";
    private static readonly DateTime Day = new(2026, 10, 15);
    private readonly Employee _employee = Employee.Create(EmployeeId.From("CIV-1"), "Ana", "Civil", null, null, new DateTime(2024, 1, 1), Gender.Female,
        BranchId.CreateNew(), DepartmentId.CreateNew(), PositionId.CreateNew(), ShiftType.Matutino);

    private AttendanceRecord Punch(int hour) =>
        AttendanceRecord.Create(_employee.Id, DeviceId.From("MANUAL"), DateTime.SpecifyKind(Day.AddHours(hour + 6), DateTimeKind.Utc),
            VerifyMethod.Manual, CheckType.CheckIn, workDate: DateOnly.FromDateTime(Day), originalCheckTime: Day.AddHours(hour), timeZoneId: Zone, source: "Manual");

    [Fact]
    public async Task AttendanceLog_ShowsTheCivilTime_NotTheUtcInstant()
    {
        var record = Punch(8);
        var attendance = new Mock<IAttendanceRepository>();
        attendance.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord> { record });
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { _employee });
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(r => r.GetAllDevicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<AttendanceSystem.Domain.Aggregates.DeviceAggregate.Device>());
        var daily = new Mock<IDailyAttendanceRepository>();
        daily.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance>());

        var handler = CreateLogsHandler(attendance.Object, employees.Object, devices.Object, daily.Object);
        var logs = (await handler.Handle(new GetAttendanceLogsQuery(Day), CancellationToken.None)).ToList();

        logs.Should().ContainSingle().Which.CheckTime.Should().Be(Day.AddHours(8));
    }

    [Fact]
    public async Task AttendanceReport_ShowsCivilEntryAndExit()
    {
        var shift = Shift.Create("Matutino", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        var entry = Punch(8);
        var exit = Punch(16);
        var day = DailyAttendance.Create(_employee.Id, Day, shift, entry.CheckTime, exit.CheckTime, false, entry.Id, exit.Id, timeZoneId: Zone);
        var daily = new Mock<IDailyAttendanceRepository>();
        daily.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { day });
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { _employee });
        var branches = new Mock<IBranchRepository>();
        branches.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Branch>());
        var departments = new Mock<IDepartmentRepository>();
        departments.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Department>());
        var positions = new Mock<IPositionRepository>();
        positions.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Position>());

        var handler = new GetAttendanceReportQueryHandler(daily.Object, employees.Object, branches.Object, departments.Object, positions.Object,
            new Mock<ISystemConfigurationRepository>().Object);
        var row = (await handler.Handle(new GetAttendanceReportQuery(Day, Day), CancellationToken.None)).Single();

        row.ActualCheckIn.Should().Be(Day.AddHours(8));
        row.ActualCheckOut.Should().Be(Day.AddHours(16));
    }

    private static GetAttendanceLogsQueryHandler CreateLogsHandler(IAttendanceRepository attendance, IEmployeeRepository employees, IDeviceRepository devices, IDailyAttendanceRepository daily) =>
        new(attendance, daily, employees, devices);
}
