using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Devices.Documentation;

/// <summary>
/// Comportamientos de los relojes que cita Docs/tecnico/04-dispositivos.md. Cada prueba fija lo que hace el código hoy,
/// aunque no sea lo deseable: si el código cambia, la prueba falla y el documento debe actualizarse.
/// </summary>
public class DispositivosEjemplosTests
{
    private readonly Mock<IDeviceRepository> _devices = new();
    private readonly Mock<IAttendanceRepository> _attendance = new();
    private readonly Mock<IDownloadLogRepository> _downloadLogs = new();
    private readonly Mock<IDeviceClientFactory> _clientFactory = new();
    private readonly Mock<IDeviceClient> _client = new();
    private readonly Mock<IAdmsCommandService> _adms = new();
    private readonly Mock<IBranchRepository> _branches = new();
    private readonly List<string> _enqueued = new();

    private DownloadFromDeviceCommandHandler Handler(Device device)
    {
        var deviceLock = new Mock<IDeviceLockService>();
        deviceLock.Setup(l => l.ExecuteWithLockAsync(It.IsAny<string>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task>, CancellationToken>((_, action, _) => action());
        _devices.Setup(r => r.GetByIdAsync(device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(device);
        _branches.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceSystem.Domain.Aggregates.BranchAggregate.Branch>());
        _clientFactory.Setup(f => f.GetClient(It.IsAny<DeviceBrand>())).Returns(_client.Object);
        _client.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _adms.Setup(a => a.EnqueueCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Guid?, CancellationToken>((_, command, _, _) => _enqueued.Add(command))
            .ReturnsAsync(Guid.NewGuid());

        // Zona por omisión del resolvedor: America/Mexico_City (UTC-6, sin horario de verano desde 2022).
        return new DownloadFromDeviceCommandHandler(
            _devices.Object, _attendance.Object, _downloadLogs.Object, _clientFactory.Object, new Mock<IUnitOfWork>().Object,
            new AttendanceDeduplicationService(), new Mock<ILogger<DownloadFromDeviceCommandHandler>>().Object,
            new Mock<IMediator>().Object, deviceLock.Object, _adms.Object, _branches.Object,
            new Mock<IAttendanceJobScheduler>().Object, new Mock<IEmployeeRepository>().Object,
            new Mock<AttendanceSystem.Domain.Repositories.IExternalAttendanceLogRepository>().Object,
            DefaultTimeNormalizer.Instance);
    }

    private static Device Adms(bool clearAfterDownload = false, string? deviceType = null)
    {
        var device = Device.Create("1", "Reloj ADMS", "192.168.0.50", 4370, DeviceBrand.ZKTeco,
            shouldClearAfterDownload: clearAfterDownload, downloadMethod: DeviceDownloadMethod.Adms, serialNumber: "SN0001");
        if (deviceType != null) device.SetDeviceType(deviceType);
        return device;
    }

    // ---------- SDK: borrar tras descargar ----------

    [Fact]
    public async Task D01_Sdk_BorrarTrasDescargar_MandaAlRelojElRangoEnUtc_NoEnSuHoraCivil()
    {
        var device = Device.Create("1", "Reloj SDK", "192.168.0.60", 4370, DeviceBrand.ZKTeco,
            shouldClearAfterDownload: true, downloadMethod: DeviceDownloadMethod.Sdk);
        // El servicio ZKTeco devuelve la hora civil del reloj (sin zona): 08:00 y 18:00 del 17/10/2026.
        var first = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Unspecified);
        var last = new DateTime(2026, 10, 17, 18, 0, 0, DateTimeKind.Unspecified);
        _client.Setup(c => c.GetAttendanceLogsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeviceLogsResult.Success(new List<RawAttendanceRecord> { new("101", first, 1, 0, 0), new("102", last, 1, 1, 0) }));
        DateTime? from = null, to = null;
        _client.Setup(c => c.ClearLogsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<string, DateTime?, DateTime?, CancellationToken>((_, f, t, _) => { from = f; to = t; })
            .ReturnsAsync(true);

        var result = await Handler(device).Handle(new DownloadFromDeviceCommand("1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // El rango es el de los marcajes guardados, pero en UTC (+6 h): el reloj recibe 14:00 a 00:00 en lugar de 08:00 a 18:00.
        from.Should().Be(new DateTime(2026, 10, 17, 14, 0, 0, DateTimeKind.Utc));
        to.Should().Be(new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc));
        from!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ---------- ADMS: la descarga es una consulta encolada ----------

    [Fact]
    public async Task D02_Adms_DescargaConFechas_EncolaUnaConsultaEnLaHoraCivilDelReloj()
    {
        var command = new DownloadFromDeviceCommand("1",
            FromDate: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Unspecified),
            ToDate: new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Unspecified));

        var result = await Handler(Adms()).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RecordsDownloaded.Should().Be(0, "los marcajes llegan después, cuando el reloj contesta la consulta");
        _enqueued.Should().Equal("DATA QUERY ATTLOG StartTime=2026-10-01 00:00:00\tEndTime=2026-10-02 00:00:00");
    }

    [Fact]
    public async Task D03_Adms_DescargaCompleta_ConsultaTodaLaTablaSinFechas()
    {
        await Handler(Adms()).Handle(new DownloadFromDeviceCommand("1", ForceFullSync: true), CancellationToken.None);

        _enqueued.Should().Equal("DATA QUERY ATTLOG StartTime=2000-01-01 00:00:00\tEndTime=2099-12-31 23:59:59");
    }

    [Fact]
    public async Task D04_Adms_RelojDeControlDeAcceso_ConsultaLaTablaTransaction()
    {
        await Handler(Adms(deviceType: "acc")).Handle(new DownloadFromDeviceCommand("1", ForceFullSync: true), CancellationToken.None);

        _enqueued.Should().Equal("DATA QUERY tablename=Transaction,fields=*");
    }

    [Fact]
    public async Task D05_Adms_BorrarTrasDescargar_SeIgnora_NoSeEncolaNingunBorrado()
    {
        await Handler(Adms(clearAfterDownload: true)).Handle(
            new DownloadFromDeviceCommand("1", ForceFullSync: true, ClearDeviceAfterDownload: true), CancellationToken.None);

        _enqueued.Should().ContainSingle().Which.Should().StartWith("DATA QUERY");
        _client.Verify(c => c.ClearLogsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    // ---------- Zona horaria en ADMS ----------

    [Theory]
    [InlineData(-6, 0, "-6")]
    [InlineData(-5, 0, "-5")]
    [InlineData(5, 30, "6")]    // India: el reloj quedaría 30 min adelantado
    [InlineData(-3, -30, "-4")] // Terranova: 30 min atrasado
    public void D06_Adms_ElDesfaseSeRedondeaAHorasEnteras(int hours, int minutes, string expected)
    {
        AdmsTimeZone.Format(new TimeSpan(hours, minutes, 0)).Should().Be(expected);
    }

    [Fact]
    public void D07_Adms_AjusteDeHora_SonDosComandos_ZonaSinHorarioDeVeranoYHoraCivil()
    {
        AdmsTimeZone.ClockCommands(new DateTime(2026, 11, 4, 9, 30, 0), TimeSpan.FromHours(-6)).Should().Equal(
            "SET OPTIONS TimeZone=-6,TZ=-6,~TimeZone=-6,DaylightSavingTime=0,~DaylightSavingTime=0",
            "SET OPTIONS DateTime=2026-11-04 09:30:00");
    }

    // ---------- Cola de comandos ADMS ----------

    [Fact]
    public void D08_ComandoNoDestructivo_SeReintentaHastaTresFallos()
    {
        var command = AdmsCommand.Create("SN0001", "0", "DATA QUERY tablename=ATTLOG,fields=*");
        command.IsDestructive.Should().BeFalse();

        for (var failure = 1; failure <= 3; failure++)
        {
            command.MarkAsSent(failure.ToString());
            command.MarkAsFailed(-1, null);
            command.RetryCount.Should().Be(failure);
            command.CanRetryAutomatically().Should().Be(failure < 3);
            if (command.CanRetryAutomatically()) command.ResetForRetry();
        }

        command.Status.Should().Be(AdmsCommandStatus.Failed);
    }

    [Theory]
    [InlineData("CLEAR ATTLOG", true)]
    [InlineData("DATA DELETE USERINFO PIN=7", true)]
    [InlineData("REBOOT", true)]
    [InlineData("DATA UPDATE USERINFO PIN=7\tName=ANA\tPri=0", false)]
    [InlineData("SET OPTIONS DateTime=2026-11-04 09:30:00", false)]
    [InlineData("INFO", false)]
    public void D09_DestructivoSeDecidePorElTexto_YNuncaSeReintenta(string text, bool destructive)
    {
        var command = AdmsCommand.Create("SN0001", "0", text);
        command.IsDestructive.Should().Be(destructive);
        command.MarkAsSent("10001");
        command.MarkAsFailed(-1, null);
        command.CanRetryAutomatically().Should().Be(!destructive);
    }

    // ---------- Política de la hora de los relojes ----------

    [Fact]
    public void D10_PoliticaPorOmision()
    {
        var policy = PunchQuarantinePolicy.Default;
        policy.MinDateDaysBeforeDevice.Should().Be(30);
        policy.FutureToleranceMinutes.Should().Be(5);
        policy.BackwardJumpMinutes.Should().Be(60);
        policy.RealtimeLagMinutes.Should().Be(30);
        policy.ClockDriftAlertMinutes.Should().Be(10);
        policy.SdkReadMarginDays.Should().Be(1);
        policy.ClockSyncToleranceMinutes.Should().Be(2);
        policy.Validate().Should().BeNull();
    }

    [Fact]
    public void D11_LaPoliticaExigeTolerancia_Menor_QueAlerta_Menor_QueCuarentena()
    {
        (PunchQuarantinePolicy.Default with { ClockDriftAlertMinutes = 40 }).Validate()
            .Should().Be("La alerta de desfase debe estar entre 1 minuto y el desfase que manda a cuarentena");
        (PunchQuarantinePolicy.Default with { ClockSyncToleranceMinutes = 6 }).Validate()
            .Should().StartWith("La tolerancia de sincronización debe estar entre 1 minuto y la menor");
    }

    [Fact]
    public void D12_FechaMinima_EsElAltaDelRelojMenosTreintaDias_SalvoQueElAdministradorFijeOtra()
    {
        var device = Device.Create("1", "Reloj", "192.168.0.60", 4370);
        var minimum = PunchTimePlausibility.MinimumDate(device, PunchQuarantinePolicy.Default);
        minimum.Should().Be(device.CreatedAt.Date.AddDays(-30));

        device.SetAcceptPunchesFrom(new DateTime(2020, 5, 1));
        PunchTimePlausibility.MinimumDate(device, PunchQuarantinePolicy.Default).Should().Be(new DateTime(2020, 5, 1));

        device.SetAcceptPunchesFrom(new DateTime(1999, 1, 1));
        PunchTimePlausibility.MinimumDate(device, PunchQuarantinePolicy.Default).Should().Be(new DateTime(2000, 1, 1), "piso absoluto");
    }
}
