using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Application.Features.Devices.Clock;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Fixtures;
using AttendanceSystem.Application.UnitTests.Features.Attendance.Phase02;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

/// <summary>
/// Reloj por SDK en Quintana Roo (UTC-5) y sistema en el centro de México (UTC-6).
/// </summary>
public class DeviceTimeZoneAttendanceTests
{
    private const string ClockZone = "America/Cancun";

    [Fact]
    public async Task SdkDownload_AsksForTheWindowInClockTime_AndCorrectsADriftedClock()
    {
        var device = Device.Create("Q1", "Reloj QRoo", "10.0.0.9", 4370, DeviceBrand.ZKTeco, timeZoneId: ClockZone);
        device.UpdateLastDownloadAt(new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc)); // 10:00 en el reloj
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(r => r.GetByIdAsync(device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(device);

        var client = new Mock<IDeviceClient>();
        client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        client.Setup(c => c.GetDeviceTimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => DefaultTimeNormalizer.Instance.ToCivilTime(DateTime.UtcNow, "America/Mexico_City")); // quedó con la hora del servidor
        client.Setup(c => c.SetDeviceClockAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        DateTime? askedFrom = null;
        client.Setup(c => c.GetAttendanceLogsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<string, DateTime?, DateTime?, CancellationToken>((_, from, _, _) => askedFrom = from)
            .ReturnsAsync(DeviceLogsResult.Empty());
        var factory = new Mock<IDeviceClientFactory>();
        factory.Setup(f => f.GetClient(It.IsAny<DeviceBrand>())).Returns(client.Object);

        var resolver = new Mock<IAuthoritativeTimeZoneResolver>();
        resolver.Setup(r => r.ResolveForDeviceAsync(It.IsAny<DeviceId?>(), It.IsAny<CancellationToken>())).ReturnsAsync(ClockZone);
        var locks = new Mock<IDeviceLockService>();
        locks.Setup(l => l.ExecuteWithLockAsync(It.IsAny<string>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task>, CancellationToken>((_, action, _) => action());
        var branches = new Mock<IBranchRepository>();
        branches.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Domain.Aggregates.BranchAggregate.Branch>());

        var handler = new DownloadFromDeviceCommandHandler(
            devices.Object, Mock.Of<IAttendanceRepository>(), Mock.Of<IDownloadLogRepository>(), factory.Object, Mock.Of<IUnitOfWork>(),
            new AttendanceDeduplicationService(), NullLogger<DownloadFromDeviceCommandHandler>.Instance, Mock.Of<IMediator>(), locks.Object,
            Mock.Of<IAdmsCommandService>(), branches.Object, Mock.Of<IAttendanceJobScheduler>(),
            Mock.Of<IEmployeeRepository>(), Mock.Of<IExternalAttendanceLogRepository>(), DefaultTimeNormalizer.Instance, resolver.Object);

        await handler.Handle(new DownloadFromDeviceCommand("Q1"), CancellationToken.None);

        askedFrom.Should().Be(new DateTime(2026, 10, 20, 10, 0, 0), "el reloj filtra con su hora: la última descarga (15:00 UTC) son las 10:00 en UTC-5");
        askedFrom!.Value.Kind.Should().Be(DateTimeKind.Unspecified);
        client.Verify(c => c.SetDeviceClockAsync(It.IsAny<DateTime>(), TimeSpan.FromHours(-5), It.IsAny<CancellationToken>()), Times.Once,
            "el reloj marcaba la hora de UTC-6: se resincroniza a su zona");
    }

    [Fact]
    public async Task Visitor_JornadaUsesTheZoneOfTheClockWhereTheEntryWasMarked()
    {
        // Empleado de una sucursal en UTC-6 que marca en un reloj de UTC-5: se calcula y muestra la hora del reloj.
        var date = new DateTime(2026, 10, 19);
        var employeeId = EmployeeId.From("EMP-VIS-1");
        var shift = Shift.Create("09-17", new TimeSpan(9, 0, 0), 10, TimeSpan.FromHours(8), ShiftType.Matutino);
        var h = new AttendanceRecalculationHarness();
        h.Store.Shifts.Add(shift);
        h.Store.Employees.Add(Employee.Create(employeeId, "Ana", "Visitante", null, null, new DateTime(2024, 1, 1), Gender.Female,
            AttendanceRegressionFixtures.BranchNorthId, AttendanceRegressionFixtures.DeptOpsId, AttendanceRegressionFixtures.PosOperatorId,
            shift.ShiftType, shift.Id, restDays: Array.Empty<WeekDay>()));
        AttendanceRecord ClockPunch(int hour, int minute, CheckType type) =>
            AttendanceRecord.Create(employeeId, DeviceId.From("Q1"), DateTime.SpecifyKind(date.AddHours(hour + 5).AddMinutes(minute), DateTimeKind.Utc),
                VerifyMethod.Fingerprint, type, workDate: DateOnly.FromDateTime(date), originalCheckTime: date.AddHours(hour).AddMinutes(minute),
                timeZoneId: ClockZone, source: "ADMS");
        h.Store.Records.Add(ClockPunch(9, 20, CheckType.CheckIn));
        h.Store.Records.Add(ClockPunch(17, 0, CheckType.CheckOut));

        await h.RecalculateAsync(date, date);

        var day = h.Daily(employeeId, date)!;
        day.TimeZoneId.Should().Be(ClockZone);
        day.CivilCheckIn.Should().Be(date.AddHours(9).AddMinutes(20), "se muestra la hora que marcó el reloj, no las 08:20 de la sucursal");
        day.CivilCheckOut.Should().Be(date.AddHours(17));
        day.LateMinutes.Should().Be(20, "el retardo se calcula con la hora del reloj contra el horario de las 09:00");
    }
}
