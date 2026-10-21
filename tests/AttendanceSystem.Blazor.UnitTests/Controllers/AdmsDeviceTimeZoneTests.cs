using System.Text;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Blazor.Server.Controllers;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Blazor.UnitTests.Controllers;

/// <summary>
/// Un reloj ADMS en UTC-5 (Quintana Roo) conectado a un sistema cuya zona es UTC-6: el saludo le envía SU zona, sus
/// marcajes se interpretan en ella y, si su hora se adelanta, se le reenvían zona y hora.
/// </summary>
public class AdmsDeviceTimeZoneTests
{
    private const string Serial = "ADMS-QROO-1";
    private const string ClockZone = "America/Cancun";

    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<IAdmsCommandService> _commands = new();
    private readonly Mock<IBranchRepository> _branches = new();
    private readonly AdmsController _controller;

    public AdmsDeviceTimeZoneTests()
    {
        var device = Device.Create("A1", "Reloj Cancún", "10.0.0.5", 8081, DeviceBrand.ZKTeco,
            downloadMethod: DeviceDownloadMethod.Adms, serialNumber: Serial, timeZoneId: ClockZone);
        _devices.Setup(r => r.GetBySerialNumberAsync(Serial, It.IsAny<CancellationToken>())).ReturnsAsync(device);
        _branches.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Domain.Aggregates.BranchAggregate.Branch>());

        var resolver = new Mock<IAuthoritativeTimeZoneResolver>();
        resolver.Setup(r => r.ResolveForDeviceAsync(It.IsAny<DeviceId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeviceId? id, CancellationToken _) => id == device.Id ? ClockZone : "America/Mexico_City");

        _controller = new AdmsController(
            NullLogger<AdmsController>.Instance, _mediator.Object, _devices.Object, Mock.Of<IUnitOfWork>(), _commands.Object,
            Mock.Of<IEmployeeRepository>(), Mock.Of<IDownloadLogRepository>(), _branches.Object, Mock.Of<ILogTransferService>(),
            Mock.Of<IAttendanceJobScheduler>(), Mock.Of<IExternalAttendanceLogRepository>(), DefaultTimeNormalizer.Instance,
            timeZoneResolver: resolver.Object);
    }

    private void Body(string content) => _controller.ControllerContext = new ControllerContext
    {
        HttpContext = new DefaultHttpContext { Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes(content)) } }
    };

    [Fact]
    public async Task Handshake_SendsTheClockZone_NotTheServerZone()
    {
        Body(string.Empty);

        var result = await _controller.Push(Serial);

        var content = result.Should().BeOfType<ContentResult>().Subject.Content!;
        content.Should().Contain("\nTimeZone=-5\n").And.Contain("\nTZ=-5\n").And.Contain("DaylightSavingTime=0");
    }

    [Fact]
    public async Task Punch_IsInterpretedInTheClockZone()
    {
        RecordAttendanceBatchCommand? sent = null;
        _mediator.Setup(m => m.Send(It.IsAny<RecordAttendanceBatchCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<int>>, CancellationToken>((c, _) => sent = (RecordAttendanceBatchCommand)c)
            .ReturnsAsync(Result<int>.Success(1));
        Body("475\t2026-03-02 09:00:00\t0\t1");

        await _controller.ReceiveData(Serial, "ATTLOG");

        var log = sent!.Logs.Should().ContainSingle().Subject;
        log.CheckTime.Should().Be(new DateTime(2026, 3, 2, 14, 0, 0, DateTimeKind.Utc), "09:00 en UTC-5 son las 14:00 UTC");
        log.OriginalCheckTime.Should().Be(new DateTime(2026, 3, 2, 9, 0, 0), "se conserva la hora que marcó el reloj");
        log.TimeZoneId.Should().Be(ClockZone);
        log.WorkDate.Should().Be(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public async Task PunchFromTheFuture_QueuesTheClockZoneAndTime()
    {
        _mediator.Setup(m => m.Send(It.IsAny<RecordAttendanceBatchCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<int>.Success(1));
        var clockNow = DefaultTimeNormalizer.Instance.ToCivilTime(DateTime.UtcNow, ClockZone);
        Body($"475\t{clockNow.AddHours(1):yyyy-MM-dd HH:mm:ss}\t0\t1");

        await _controller.ReceiveData(Serial, "ATTLOG");

        _commands.Verify(c => c.EnqueueCommand(Serial, It.Is<string>(cmd => cmd.StartsWith("SET OPTIONS TimeZone=-5,")), It.IsAny<Guid?>()), Times.Once);
        _commands.Verify(c => c.EnqueueCommand(Serial, It.Is<string>(cmd => cmd.StartsWith("SET OPTIONS DateTime=")), It.IsAny<Guid?>()), Times.Once);
    }

    [Fact]
    public async Task PunchFromThePast_DoesNotTouchTheClock()
    {
        _mediator.Setup(m => m.Send(It.IsAny<RecordAttendanceBatchCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<int>.Success(1));
        Body("475\t2026-03-02 09:00:00\t0\t1");

        await _controller.ReceiveData(Serial, "ATTLOG");

        _commands.Verify(c => c.EnqueueCommand(It.IsAny<string>(), It.Is<string>(cmd => cmd.StartsWith("SET OPTIONS")), It.IsAny<Guid?>()), Times.Never);
    }
}
