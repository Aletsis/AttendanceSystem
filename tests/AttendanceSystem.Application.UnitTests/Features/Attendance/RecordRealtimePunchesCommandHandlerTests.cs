using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordRealtimePunches;
using AttendanceSystem.Application.Features.Devices.Quarantine;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalLogAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

/// <summary>
/// Marcajes que un reloj SDK envía en tiempo real: se guardan como los de una descarga, pero como llegada en tiempo real.
/// </summary>
public class RecordRealtimePunchesCommandHandlerTests
{
    private const string ClockZone = "America/Cancun";
    private static readonly DateTime Civil = new(2026, 11, 3, 20, 25, 6);

    private readonly Device _device = Device.Create("Q1", "Reloj QRoo", "10.0.0.9", 4370, DeviceBrand.ZKTeco, serialNumber: "AYK8241300034", timeZoneId: ClockZone);
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IExternalAttendanceLogRepository> _externalLogs = new();
    private readonly Mock<IDeviceHeartbeatTracker> _heartbeats = new();
    private readonly List<Branch> _branches = new();
    private RecordAttendanceBatchCommand? _sent;

    public RecordRealtimePunchesCommandHandlerTests()
    {
        _mediator.Setup(m => m.Send(It.IsAny<RecordAttendanceBatchCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<int>>, CancellationToken>((c, _) => _sent = (RecordAttendanceBatchCommand)c)
            .ReturnsAsync((IRequest<Result<int>> c, CancellationToken _) => Result<int>.Success(((RecordAttendanceBatchCommand)c).Logs.Count));
        _externalLogs.Setup(r => r.ExcludeAlreadyStoredAsync(It.IsAny<IReadOnlyList<ExternalAttendanceLog>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ExternalAttendanceLog> logs, CancellationToken _) => logs);
    }

    [Fact]
    public async Task LocalPunches_AreRecordedAsRealtimeArrivals_InTheClockZone()
    {
        var result = await Handler().Handle(Command(new RealtimePunchDto("1821", Civil, 1, 0)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        _sent.Should().NotBeNull();
        _sent!.Arrival.Should().Be(PunchArrival.Realtime, "la cuarentena aplica la regla R4 de reloj atrasado en tiempo real");
        _sent.DeviceSerial.Should().BeNull("con número de serie, un reloj atrasado recibiría comandos ADMS que un reloj SDK no recoge");
        _sent.TimeZoneId.Should().Be(ClockZone);
        var log = _sent.Logs.Should().ContainSingle().Subject;
        log.EmployeeId.Should().Be("1821");
        log.OriginalCheckTime.Should().Be(Civil);
        log.CheckTime.Should().Be(new DateTime(2026, 11, 4, 1, 25, 6, DateTimeKind.Utc), "UTC-5: la hora civil del reloj más 5 horas");
        log.WorkDate.Should().Be(new DateOnly(2026, 11, 3));
        log.Source.Should().Be("ZKTeco", "igual que los marcajes descargados de ese reloj");
    }

    [Fact]
    public async Task ExternalBranchPunches_WaitForTheCloud_AndDoNotReachLocalAttendance()
    {
        _branches.Add(Branch.Create("C01", "Cancún", null, isExternal: true));
        ExternalAttendanceLog? stored = null;
        _externalLogs.Setup(r => r.AddAsync(It.IsAny<ExternalAttendanceLog>(), It.IsAny<CancellationToken>()))
            .Callback<ExternalAttendanceLog, CancellationToken>((l, _) => stored = l);

        var result = await Handler().Handle(Command(new RealtimePunchDto("c01045", Civil, 1, 0)), CancellationToken.None);

        result.Value.Should().Be(1);
        stored.Should().NotBeNull();
        stored!.BranchCode.Should().Be("C01", "el prefijo se compara sin distinguir mayúsculas, como en ADMS y la descarga");
        stored.EmployeeId.Should().Be("045");
        stored.CheckTime.Should().Be(new DateTime(2026, 11, 4, 1, 25, 6, DateTimeKind.Utc));
        _sent.Should().BeNull();
    }

    [Fact]
    public async Task AlreadyStoredExternalPunches_AreNotAddedAgain()
    {
        _branches.Add(Branch.Create("C01", "Cancún", null, isExternal: true));
        _externalLogs.Setup(r => r.ExcludeAlreadyStoredAsync(It.IsAny<IReadOnlyList<ExternalAttendanceLog>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ExternalAttendanceLog>());

        var result = await Handler().Handle(Command(new RealtimePunchDto("C01045", Civil, 1, 0)), CancellationToken.None);

        result.Value.Should().Be(0);
        _externalLogs.Verify(r => r.AddAsync(It.IsAny<ExternalAttendanceLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Punches_KeepTheClockMarkedAsCommunicating()
    {
        await Handler().Handle(Command(new RealtimePunchDto("1821", Civil, 1, 0)), CancellationToken.None);

        _heartbeats.Verify(h => h.RecordActivity("AYK8241300034", _device.Id.Value), Times.Once);
    }

    [Fact]
    public async Task InactiveClock_IsIgnored()
    {
        _device.Deactivate();

        var result = await Handler().Handle(Command(new RealtimePunchDto("1821", Civil, 1, 0)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _sent.Should().BeNull();
    }

    [Fact]
    public async Task FailedBatch_IsReported()
    {
        _mediator.Setup(m => m.Send(It.IsAny<RecordAttendanceBatchCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Failure("base de datos no disponible"));

        var result = await Handler().Handle(Command(new RealtimePunchDto("1821", Civil, 1, 0)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse("quien lo envió pide entonces una descarga de respaldo");
    }

    private RecordRealtimePunchesCommand Command(params RealtimePunchDto[] punches) => new(_device.Id.Value, punches);

    private RecordRealtimePunchesCommandHandler Handler()
    {
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(r => r.GetByIdAsync(It.Is<DeviceId>(id => id.Value == _device.Id.Value), It.IsAny<CancellationToken>())).ReturnsAsync(_device);
        var branches = new Mock<IBranchRepository>();
        branches.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _branches);
        var resolver = new Mock<IAuthoritativeTimeZoneResolver>();
        resolver.Setup(r => r.ResolveForDeviceAsync(It.IsAny<DeviceId?>(), It.IsAny<CancellationToken>())).ReturnsAsync(ClockZone);

        return new RecordRealtimePunchesCommandHandler(
            devices.Object, branches.Object, _externalLogs.Object, Mock.Of<IUnitOfWork>(), DefaultTimeNormalizer.Instance,
            resolver.Object, _mediator.Object, NullLogger<RecordRealtimePunchesCommandHandler>.Instance, _heartbeats.Object);
    }
}
