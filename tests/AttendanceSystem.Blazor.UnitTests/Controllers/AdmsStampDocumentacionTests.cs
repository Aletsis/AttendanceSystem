using System.Text;
using AttendanceSystem.Adms;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Blazor.UnitTests.Controllers;

/// <summary>
/// Docs/tecnico/04-dispositivos.md: de dónde sale el Stamp que se envía a un reloj ADMS. El cursor es
/// <c>Devices.AdmsStamp</c>, conservado siempre en la hora civil del reloj y desacoplado de <c>Devices.LastDownloadAt</c>.
/// </summary>
public class AdmsStampDocumentacionTests
{
    private const string Sn = "SNDOC0001";
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<IAdmsCommandService> _adms = new();
    private readonly Mock<IDownloadLogRepository> _downloadLogs = new();
    private readonly Mock<ITimeNormalizer> _time = new();
    private readonly Device _device;
    private readonly AdmsController _controller;

    public AdmsStampDocumentacionTests()
    {
        // Zona UTC para que la conversión no altere las horas y se vea sólo el origen del valor.
        _time.Setup(t => t.ToUtc(It.IsAny<DateTime>(), It.IsAny<string>())).Returns<DateTime, string?>((dt, _) => DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        _time.Setup(t => t.ToWorkDate(It.IsAny<DateTime>(), It.IsAny<string>())).Returns<DateTime, string?>((dt, _) => DateOnly.FromDateTime(dt));
        _time.Setup(t => t.ToCivilTime(It.IsAny<DateTime>(), It.IsAny<string>())).Returns<DateTime, string?>((dt, _) => DateTime.SpecifyKind(dt, DateTimeKind.Unspecified));
        _time.Setup(t => t.ResolveTimeZone(It.IsAny<string>())).Returns(TimeZoneInfo.Utc);

        _device = Device.Create("1", "Reloj ADMS", "192.168.0.205", 8083, DeviceBrand.ZKTeco,
            downloadMethod: DeviceDownloadMethod.Adms, serialNumber: Sn);
        _devices.Setup(r => r.GetBySerialNumberAsync(Sn, default)).ReturnsAsync(_device);
        _mediator.Setup(m => m.Send(It.IsAny<RecordAttendanceBatchCommand>(), default)).ReturnsAsync(Result<int>.Success(1));

        _controller = new AdmsController(
            new Mock<ILogger<AdmsController>>().Object, _mediator.Object, _devices.Object, new Mock<IUnitOfWork>().Object,
            _adms.Object, new Mock<IEmployeeRepository>().Object, _downloadLogs.Object, new Mock<IBranchRepository>().Object,
            new Mock<ILogTransferService>().Object, new Mock<IAttendanceJobScheduler>().Object,
            new Mock<IExternalAttendanceLogRepository>().Object, _time.Object);
    }

    private void Body(string content) => _controller.ControllerContext = new ControllerContext
    {
        HttpContext = new DefaultHttpContext { Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes(content)) } }
    };

    [Fact]
    public async Task S01_LoteLimpio_GuardaComoStampLaHoraCivilDelUltimoMarcaje()
    {
        Body("475\t2026-03-01 08:00:00\t0\t1\n475\t2026-03-01 13:05:00\t1\t1");

        await _controller.ReceiveData(Sn, "ATTLOG");

        _devices.Verify(r => r.UpdateLastAttLogTimestampAsync(Sn,
            It.Is<DateTime>(d => d == new DateTime(2026, 3, 1, 13, 5, 0) && d.Kind == DateTimeKind.Unspecified), default), Times.Once);
    }

    [Fact]
    public async Task S02_LoteConUnaLineaMala_NoAvanzaElStampAdmsNiLoContaminaConUtc()
    {
        Body("475\t2026-03-01 08:00:00\t0\t1\nlinea_mal_formada");
        var before = DateTime.UtcNow;

        await _controller.ReceiveData(Sn, "ATTLOG");

        // El cursor ADMS "no avanza" (no se llama a UpdateLastAttLogTimestampAsync) y no se contamina con UTC.
        _devices.Verify(r => r.UpdateLastAttLogTimestampAsync(It.IsAny<string>(), It.IsAny<DateTime>(), default), Times.Never());
        _device.AdmsStamp.Should().BeNull();

        // LastDownloadAt registra la auditoría de recepción en UTC, desacoplado de AdmsStamp.
        _device.LastDownloadAt.Should().NotBeNull();
        _device.LastDownloadAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
        _device.LastDownloadAt.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public async Task S03_AcuseDeUnaConsulta_ActualizaLastDownloadAtPeroNoTocaElStampAdms()
    {
        var hasta = new DateTime(2026, 3, 2, 18, 0, 0, DateTimeKind.Utc);
        var log = DownloadLog.Create(_device.Id, DownloadType.Manual, null, null, null, hasta);
        _adms.Setup(s => s.AcknowledgeCommandExecutionAsync(Sn, "10001", 0, null, default)).ReturnsAsync(log.Id.Value);
        _adms.Setup(s => s.GetAndCompleteDownloadSession(Sn, "10001")).Returns((3, 3));
        _downloadLogs.Setup(r => r.GetByIdAsync(It.Is<DownloadLogId>(id => id.Value == log.Id.Value), default)).ReturnsAsync(log);
        Body("ID=10001&Return=0&CMD=DATA QUERY");

        await _controller.DeviceCmd(Sn);

        _device.LastDownloadAt.Should().Be(hasta);
        _device.AdmsStamp.Should().BeNull();
        _devices.Verify(r => r.UpdateLastAttLogTimestampAsync(It.IsAny<string>(), It.IsAny<DateTime>(), default), Times.Never());
    }

    [Fact]
    public async Task S04_LasOpcionesEnvianElValorDelStampAdms_EnHoraCivil()
    {
        _devices.Setup(r => r.GetLastAttLogTimestampAsync(Sn, default))
            .ReturnsAsync(new DateTime(2026, 3, 2, 18, 0, 0, DateTimeKind.Unspecified));
        Body("");

        var result = await _controller.Push(Sn);

        var content = result.Should().BeOfType<ContentResult>().Subject.Content!;
        content.Should().Contain("\nStamp=2026-03-02 18:00:00\n").And.Contain("\nATTLOGStamp=2026-03-02 18:00:00\n");
    }

    [Fact]
    public async Task S05_SinStampGuardado_PideTodoDesde1970()
    {
        Body("");

        var result = await _controller.Push(Sn);

        result.Should().BeOfType<ContentResult>().Subject.Content.Should().Contain("\nATTLOGStamp=1970-01-01 00:00:00\n");
    }

    [Fact]
    public async Task S06_Registry_ReiniciaElStampAdms_SinBorrarLastDownloadAt()
    {
        var prevDownload = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        _device.RecordSuccessfulDownload(10, prevDownload);
        _device.UpdateAdmsStamp(new DateTime(2026, 3, 1, 6, 0, 0, DateTimeKind.Unspecified));
        Body("DeviceType=att");

        var result = await _controller.Registry(Sn);

        result.Should().BeOfType<ContentResult>();
        _device.AdmsStamp.Should().BeNull("el registro del reloj debe reiniciar el cursor ADMS");
        _device.LastDownloadAt.Should().Be(prevDownload, "el registro del reloj no debe destruir el historial de descargas");
    }
}
