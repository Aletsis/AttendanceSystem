using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

public class AdmsCommandServiceTests
{
    private readonly ServiceProvider _serviceProvider;
    private readonly DbContextOptions<AttendanceDbContext> _dbOptions;
    private readonly Mock<IPublisher> _publisherMock = new();

    public AdmsCommandServiceTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AttendanceDbContext>()
            .UseInMemoryDatabase(databaseName: $"AdmsCommandTests_{Guid.NewGuid()}")
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => new AttendanceDbContext(_dbOptions, _publisherMock.Object));
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task DeviceInfo_OnlyCompletesForMatchingSerialAndDeliveredCommand()
    {
        var service = new AdmsCommandService(_serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<AdmsCommandService>>());
        var info = new AttendanceSystem.Application.DTOs.DeviceInfoDto(
            "SN_INFO", "MiniAC Plus", "1.0", "ZK", 1, 0, 0, 2, 100, 0, 0, 100);
        using var cancellation = new CancellationTokenSource();
        var pending = service.WaitForDeviceInfoAsync("SN_INFO", TimeSpan.FromSeconds(5), cancellation.Token);
        try
        {
            service.NotifyDeviceInfoReceived("SN_INFO", info);
            pending.IsCompleted.Should().BeFalse("unsolicited options do not answer an INFO request");

            service.EnqueueCommand("SN_INFO", "INFO");
            var (id, _, _) = service.GetNextCommand("SN_INFO");
            service.NotifyDeviceInfoReceived("OTHER", info, id);
            service.NotifyDeviceInfoReceived("SN_INFO", info, "wrong-id");
            pending.IsCompleted.Should().BeFalse();

            service.NotifyDeviceInfoReceived("sn_info", info, id);
            (await pending).Should().BeSameAs(info);
        }
        finally
        {
            await cancellation.CancelAsync();
        }
    }

    [Fact]
    public async Task DeviceInfo_LateReplyDoesNotCompleteNewRequest()
    {
        var service = new AdmsCommandService(_serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<AdmsCommandService>>());
        var info = new AttendanceSystem.Application.DTOs.DeviceInfoDto(
            "SN_LATE", "MiniAC Plus", "1.0", "ZK", 1, 0, 0, 2, 100, 0, 0, 100);
        using var oldCancellation = new CancellationTokenSource();
        var oldRequest = service.WaitForDeviceInfoAsync("SN_LATE", TimeSpan.FromSeconds(5), oldCancellation.Token);
        service.EnqueueCommand("SN_LATE", "INFO");
        var (oldId, _, _) = service.GetNextCommand("SN_LATE");
        await oldCancellation.CancelAsync();
        (await oldRequest).Should().BeNull();

        using var cancellation = new CancellationTokenSource();
        var pending = service.WaitForDeviceInfoAsync("SN_LATE", TimeSpan.FromSeconds(5), cancellation.Token);
        try
        {
            service.NotifyDeviceInfoReceived("SN_LATE", info, oldId);
            pending.IsCompleted.Should().BeFalse();
        }
        finally
        {
            await cancellation.CancelAsync();
        }
        (await pending).Should().BeNull();
    }

    [Fact]
    public void GetNextCommand_GeneratesUniqueNonCollidingIdsInRange10000To99999_H20()
    {
        // Arrange
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var loggerMock = new Mock<ILogger<AdmsCommandService>>();
        var service = new AdmsCommandService(scopeFactory, loggerMock.Object);

        var sn = "SNTEST100";
        for (int i = 0; i < 20; i++)
        {
            service.EnqueueCommand(sn, $"DATA QUERY ATTLOG {i}");
        }

        var commandIds = new HashSet<string>();

        // Act
        for (int i = 0; i < 20; i++)
        {
            var (cmdId, cmdText, _) = service.GetNextCommand(sn);
            cmdId.Should().NotBeNullOrEmpty();
            int.TryParse(cmdId, out var numId).Should().BeTrue();
            numId.Should().BeInRange(10000, 99999);
            commandIds.Add(cmdId!);
        }

        // Assert: Todos los IDs generados son únicos (sin colisiones)
        commandIds.Should().HaveCount(20);
    }

    [Fact]
    public void AdmsCommand_DestructiveCommand_CannotRetryAutomatically_H20()
    {
        // Arrange
        var destructiveCmd = AdmsCommand.Create("SN123", "10001", "CLEAR LOG");
        var nonDestructiveCmd = AdmsCommand.Create("SN123", "10002", "DATA QUERY ATTLOG");

        destructiveCmd.MarkAsFailed(-1, "Error de hardware");
        nonDestructiveCmd.MarkAsFailed(-1, "Timeout transitorio");

        // Act & Assert
        destructiveCmd.IsDestructive.Should().BeTrue("CLEAR LOG es una operación destructiva");
        destructiveCmd.CanRetryAutomatically().Should().BeFalse("operaciones destructivas nunca deben reintentarse automáticamente");

        nonDestructiveCmd.IsDestructive.Should().BeFalse();
        nonDestructiveCmd.CanRetryAutomatically().Should().BeTrue("consultas de datos son idempotentes y pueden reintentarse");
    }

    [Fact]
    public async Task AcknowledgeCommandExecutionAsync_WhenDestructiveCommandFails_DoesNotRequeue()
    {
        // Arrange
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var loggerMock = new Mock<ILogger<AdmsCommandService>>();
        var service = new AdmsCommandService(scopeFactory, loggerMock.Object);

        var sn = "SN_CLEAR_TEST";
        service.EnqueueCommand(sn, "CLEAR LOG");

        var (cmdId, _, _) = service.GetNextCommand(sn);
        cmdId.Should().NotBeNull();

        // Act: El dispositivo reporta fallo (-1)
        await service.AcknowledgeCommandExecutionAsync(sn, cmdId!, -1, "Error reloj");

        // Assert: La cola debe estar vacía (no se reencola automáticamente comando destructivo)
        service.HasPendingCommands(sn).Should().BeFalse();
    }

    [Fact]
    public void DownloadSession_AccumulatesRecordsAndNotifiesOnCompletion()
    {
        // Arrange
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var service = new AdmsCommandService(scopeFactory, Mock.Of<ILogger<AdmsCommandService>>());
        var sn = "SN_DOWNLOAD_SESSION";
        var logId = Guid.NewGuid();

        service.EnqueueCommand(sn, "DATA QUERY tablename=ATTLOG", logId);
        var (cmdId, _, returnedLogId) = service.GetNextCommand(sn);
        returnedLogId.Should().Be(logId);

        // Act & Assert active session lookup
        var activeLogId = service.GetActiveDownloadLogId(sn, cmdId);
        activeLogId.Should().Be(logId);

        // Simular dos paquetes de querydata
        service.RecordDownloadProgress(sn, cmdId, totalReceived: 10, newInserted: 8);
        service.RecordDownloadProgress(sn, cmdId, totalReceived: 15, newInserted: 12);

        // Event listener
        string? notifiedSn = null;
        Guid? notifiedLogId = null;
        bool? notifiedSuccess = null;
        int notifiedTotal = 0, notifiedNew = 0;

        service.OnDownloadStatusChanged += (s, lId, succ, tot, nw, err) =>
        {
            notifiedSn = s;
            notifiedLogId = lId;
            notifiedSuccess = succ;
            notifiedTotal = tot;
            notifiedNew = nw;
        };

        var (totalReceived, newInserted) = service.GetAndCompleteDownloadSession(sn, cmdId!);
        totalReceived.Should().Be(25);
        newInserted.Should().Be(20);

        service.NotifyDownloadCompleted(sn, logId, isSuccess: true, totalReceived, newInserted);

        notifiedSn.Should().Be(sn);
        notifiedLogId.Should().Be(logId);
        notifiedSuccess.Should().BeTrue();
        notifiedTotal.Should().Be(25);
        notifiedNew.Should().Be(20);

        // Tras completar, ya no hay sesión activa
        service.GetActiveDownloadLogId(sn, cmdId).Should().BeNull();
    }
}
