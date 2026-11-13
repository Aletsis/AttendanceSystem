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

    [Fact]
    public async Task AttendanceLog_IsSplitShift_ReflectsShiftType()
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

        // Caso regular: IsSplitShift = false
        daily.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance>());

        var handler = CreateLogsHandler(attendance.Object, employees.Object, devices.Object, daily.Object);
        var logs = (await handler.Handle(new GetAttendanceLogsQuery(Day), CancellationToken.None)).ToList();
        logs.Should().ContainSingle().Which.IsSplitShift.Should().BeFalse();

        // Caso partido: IsSplitShift = true
        var splitShift = Shift.Create("Partido", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(4), ShiftType.Partido,
            secondBlockStartTime: new TimeSpan(14, 0, 0), secondBlockEndTime: new TimeSpan(18, 0, 0));
        var splitDay = DailyAttendance.Create(_employee.Id, Day, splitShift, record.CheckTime, null, false, record.Id, null, timeZoneId: Zone);
        daily.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { splitDay });

        var splitLogs = (await handler.Handle(new GetAttendanceLogsQuery(Day), CancellationToken.None)).ToList();
        splitLogs.Should().ContainSingle().Which.IsSplitShift.Should().BeTrue();
    }

    [Fact]
    public async Task AttendanceLog_TemporaryExitAndReturn_AreMappedCorrectly()
    {
        var punchIn = Punch(8);
        var punchExit = Punch(13);
        var punchReturn = Punch(14);
        var punchOut = Punch(17);

        var attendance = new Mock<IAttendanceRepository>();
        attendance.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord> { punchIn, punchExit, punchReturn, punchOut });
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { _employee });
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(r => r.GetAllDevicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<AttendanceSystem.Domain.Aggregates.DeviceAggregate.Device>());

        var shift = Shift.Create("Regular", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        var dailyAttendance = DailyAttendance.Create(_employee.Id, Day, shift, punchIn.CheckTime, punchOut.CheckTime, false, punchIn.Id, punchOut.Id, timeZoneId: Zone);

        var temporaryExit = new TemporaryExit(punchExit.Id.Value, Day.AddHours(13), Day.AddHours(14), 60, TemporaryExitStatus.ApprovedPaid, IsAutomaticLunch: false);
        dailyAttendance.ApplyIntermediateAnalysis(new List<TemporaryExit> { temporaryExit }, 30);

        var daily = new Mock<IDailyAttendanceRepository>();
        daily.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { dailyAttendance });

        var handler = CreateLogsHandler(attendance.Object, employees.Object, devices.Object, daily.Object);
        var logs = (await handler.Handle(new GetAttendanceLogsQuery(Day), CancellationToken.None)).ToList();

        var exitLog = logs.Single(l => l.Id == punchExit.Id.Value);
        exitLog.IsTemporaryExit.Should().BeTrue();
        exitLog.TemporaryExitStatus.Should().Be(TemporaryExitStatus.Pending); // AutomaticStatus for non-lunch is Pending until adjusted
        exitLog.TemporaryExitMinutes.Should().Be(60);
        exitLog.TemporaryExitReturnTime.Should().Be(Day.AddHours(14));
        exitLog.EntryType.Should().Contain("Salida temporal");

        var returnLog = logs.Single(l => l.Id == punchReturn.Id.Value);
        returnLog.IsTemporaryExitReturn.Should().BeTrue();
        returnLog.TemporaryExitMinutes.Should().Be(60);
        returnLog.EntryType.Should().Be("Regreso de salida");
    }

    [Fact]
    public async Task AttendanceLog_WithAdjustmentAudit_MapsDayRevisionAndNote()
    {
        var punchExit = Punch(13);
        var attendance = new Mock<IAttendanceRepository>();
        attendance.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord> { punchExit });
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee> { _employee });
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(r => r.GetAllDevicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<AttendanceSystem.Domain.Aggregates.DeviceAggregate.Device>());

        var shift = Shift.Create("Regular", new TimeSpan(8, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        var dailyAttendance = DailyAttendance.Create(_employee.Id, Day, shift, null, null, false, null, null, timeZoneId: Zone);
        var temporaryExit = new TemporaryExit(punchExit.Id.Value, Day.AddHours(13), Day.AddHours(14), 60, TemporaryExitStatus.ApprovedPaid, IsAutomaticLunch: false);
        dailyAttendance.ApplyIntermediateAnalysis(new List<TemporaryExit> { temporaryExit }, 30);

        var daily = new Mock<IDailyAttendanceRepository>();
        daily.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyAttendance> { dailyAttendance });

        var adjustment = AttendanceAdjustment.Create(_employee.Id, Day, "rh_user");
        adjustment.ClassifyExit(punchExit.Id.Value, TemporaryExitStatus.ApprovedPaid, "rh_user", "Cita medica");

        var adjustmentsRepo = new Mock<IAttendanceAdjustmentRepository>();
        adjustmentsRepo.Setup(r => r.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BranchId?>(), It.IsAny<EmployeeId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceAdjustment> { adjustment });

        var handler = CreateLogsHandler(attendance.Object, employees.Object, devices.Object, daily.Object, adjustmentsRepo.Object);
        var logs = (await handler.Handle(new GetAttendanceLogsQuery(Day), CancellationToken.None)).ToList();

        var exitLog = logs.Single(l => l.Id == punchExit.Id.Value);
        exitLog.DayRevision.Should().Be(adjustment.Revision);
        exitLog.TemporaryExitNote.Should().Be("Cita medica");
    }

    private static GetAttendanceLogsQueryHandler CreateLogsHandler(
        IAttendanceRepository attendance,
        IEmployeeRepository employees,
        IDeviceRepository devices,
        IDailyAttendanceRepository daily,
        IAttendanceAdjustmentRepository? adjustments = null) =>
        new(attendance, daily, employees, devices, null, adjustments);
}
