using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalLogAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Attendance;

public class DownloadFromDeviceSafetyTests
{
    private readonly Mock<IDeviceRepository> _deviceRepositoryMock = new();
    private readonly Mock<IAttendanceRepository> _attendanceRepositoryMock = new();
    private readonly Mock<IDownloadLogRepository> _downloadLogRepositoryMock = new();
    private readonly Mock<IDeviceClientFactory> _clientFactoryMock = new();
    private readonly Mock<IDeviceClient> _deviceClientMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ILogger<DownloadFromDeviceCommandHandler>> _loggerMock = new();
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly Mock<IDeviceLockService> _deviceLockServiceMock = new();
    private readonly Mock<IAdmsCommandService> _admsCommandServiceMock = new();
    private readonly Mock<IBranchRepository> _branchRepositoryMock = new();
    private readonly Mock<ILogTransferService> _logTransferServiceMock = new();
    private readonly Mock<IAttendanceJobScheduler> _jobSchedulerMock = new();
    private readonly Mock<IEmployeeRepository> _employeeRepositoryMock = new();
    private readonly Mock<IExternalAttendanceLogRepository> _externalLogRepositoryMock = new();
    private readonly Mock<ITimeNormalizer> _timeNormalizerMock = new();
    private readonly AttendanceDeduplicationService _deduplicationService;

    private readonly Device _device;
    private readonly DownloadFromDeviceCommandHandler _handler;

    public DownloadFromDeviceSafetyTests()
    {
        _deduplicationService = new AttendanceDeduplicationService();

        // Passthrough para device lock service
        _deviceLockServiceMock
            .Setup(l => l.ExecuteWithLockAsync(It.IsAny<string>(), It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task>, CancellationToken>((_, action, _) => action());

        _clientFactoryMock.Setup(f => f.GetClient(It.IsAny<DeviceBrand>())).Returns(_deviceClientMock.Object);

        _deviceClientMock
            .Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _timeNormalizerMock.Setup(t => t.ToUtc(It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns<DateTime, string?>((dt, _) => DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        _timeNormalizerMock.Setup(t => t.ToWorkDate(It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns<DateTime, string?>((dt, _) => DateOnly.FromDateTime(dt));

        _device = Device.Create(
            "1",
            "Reloj Puerta Principal",
            "192.168.1.200",
            4370,
            DeviceBrand.ZKTeco,
            downloadMethod: DeviceDownloadMethod.Sdk,
            shouldClearAfterDownload: true);

        _deviceRepositoryMock.Setup(r => r.GetByIdAsync(_device.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_device);

        _branchRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceSystem.Domain.Aggregates.BranchAggregate.Branch>());

        _attendanceRepositoryMock.Setup(r => r.GetByDeviceAndDateRangeAsync(It.IsAny<DeviceId>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceRecord>());

        _mediatorMock.Setup(m => m.Send(It.IsAny<RecordAttendanceBatchCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(1));

        _handler = new DownloadFromDeviceCommandHandler(
            _deviceRepositoryMock.Object,
            _attendanceRepositoryMock.Object,
            _downloadLogRepositoryMock.Object,
            _clientFactoryMock.Object,
            _unitOfWorkMock.Object,
            _deduplicationService,
            _loggerMock.Object,
            _mediatorMock.Object,
            _deviceLockServiceMock.Object,
            _admsCommandServiceMock.Object,
            _branchRepositoryMock.Object,
            _logTransferServiceMock.Object,
            _jobSchedulerMock.Object,
            _employeeRepositoryMock.Object,
            _externalLogRepositoryMock.Object,
            _timeNormalizerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenReadStatusIsFailed_NeverClearsLogsAndDoesNotAdvanceCursor_H03()
    {
        // Arrange
        _deviceClientMock.Setup(c => c.GetAttendanceLogsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeviceLogsResult.Failed("Error de socket o timeout en el reloj"));

        var initialLastDownload = _device.LastDownloadAt;
        var command = new DownloadFromDeviceCommand("1");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert: Retorna fallo explícito
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("timeout");

        // H03: El cliente NO debe haber llamado a ClearLogsAsync
        _deviceClientMock.Verify(c => c.ClearLogsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never());

        // H03: El cursor no debe haberse modificado
        _device.LastDownloadAt.Should().Be(initialLastDownload);
    }

    [Fact]
    public async Task Handle_WhenReadStatusIsPartial_SavesRecordsIdempotently_SkipsClearingLogsAndDoesNotAdvanceCursor_H03()
    {
        // Arrange
        var rawRecords = new List<RawAttendanceRecord>
        {
            new("101", new DateTime(2026, 10, 17, 8, 30, 0, DateTimeKind.Utc), 1, 0, 0)
        };

        _deviceClientMock.Setup(c => c.GetAttendanceLogsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeviceLogsResult.Partial(rawRecords, "Lectura incompleta por desconexión en bloque 2"));

        var initialLastDownload = _device.LastDownloadAt;
        var command = new DownloadFromDeviceCommand("1");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ReadStatus.Should().Be(DeviceReadStatus.Partial);
        result.Value.LogsCleared.Should().BeFalse("descarga parcial debe preservar registros en el dispositivo físico");
        result.Value.ClearErrorMessage.Should().ContainEquivalentOf("lectura parcial");

        // H03: ClearLogsAsync NUNCA se invoca si el resultado es parcial
        _deviceClientMock.Verify(c => c.ClearLogsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never());

        // H03: Cursor no se adelanta
        _device.LastDownloadAt.Should().Be(initialLastDownload);
    }

    [Fact]
    public async Task Handle_WhenReadStatusIsSuccessWithData_ClearsLogsOnlyWithBoundedRange_H03()
    {
        // Arrange
        var minDate = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);
        var maxDate = new DateTime(2026, 10, 17, 18, 0, 0, DateTimeKind.Utc);
        var rawRecords = new List<RawAttendanceRecord>
        {
            new("101", minDate, 1, 0, 0),
            new("102", maxDate, 1, 1, 0)
        };

        _deviceClientMock.Setup(c => c.GetAttendanceLogsAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeviceLogsResult.Success(rawRecords));

        _deviceClientMock.Setup(c => c.ClearLogsAsync("1", minDate, maxDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new DownloadFromDeviceCommand("1");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ReadStatus.Should().Be(DeviceReadStatus.SuccessWithData);
        result.Value.LogsCleared.Should().BeTrue();

        // H03: Verifica que ClearLogsAsync fue invocado estrictamente con el rango acotado [minDate, maxDate]
        _deviceClientMock.Verify(c => c.ClearLogsAsync("1", minDate, maxDate, It.IsAny<CancellationToken>()), Times.Once());
    }
}
