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

    private AdmsCommandService NewService() =>
        new(_serviceProvider.GetRequiredService<IServiceScopeFactory>(), Mock.Of<ILogger<AdmsCommandService>>());

    private static AttendanceSystem.Application.DTOs.DeviceInfoDto Info(string sn) =>
        new(sn, "MiniAC Plus", "1.0", "ZK", 1, 0, 0, 2, 100, 0, 0, 100);

    [Fact]
    public async Task DeviceInfo_OnlyCompletesForMatchingSerialAndDeliveredCommand()
    {
        var service = NewService();
        var info = Info("SN_INFO");

        var infoCommand = await service.EnqueueCommandAsync("SN_INFO", "INFO");
        var pending = service.WaitForDeviceInfoAsync(infoCommand, TimeSpan.FromSeconds(5));

        await service.NotifyDeviceInfoReceivedAsync("SN_INFO", info);
        var (id, _, _) = await service.GetNextCommandAsync("SN_INFO");
        await service.NotifyDeviceInfoReceivedAsync("OTHER", info, id);
        await service.NotifyDeviceInfoReceivedAsync("SN_INFO", info, "wrong-id");
        await Task.Delay(700);
        pending.IsCompleted.Should().BeFalse("ni opciones sin solicitar, ni otro reloj, ni otro CommandID contestan este INFO");

        await service.NotifyDeviceInfoReceivedAsync("sn_info", info, id);
        (await pending).Should().Be(info);
    }

    [Fact]
    public async Task DeviceInfo_LateReplyDoesNotCompleteNewRequest()
    {
        var service = NewService();
        var info = Info("SN_LATE");

        var oldCommand = await service.EnqueueCommandAsync("SN_LATE", "INFO");
        var (oldId, _, _) = await service.GetNextCommandAsync("SN_LATE");
        (await service.WaitForDeviceInfoAsync(oldCommand, TimeSpan.FromMilliseconds(100))).Should().BeNull();

        var newCommand = await service.EnqueueCommandAsync("SN_LATE", "INFO");
        var pending = service.WaitForDeviceInfoAsync(newCommand, TimeSpan.FromSeconds(2));
        await service.NotifyDeviceInfoReceivedAsync("SN_LATE", info, oldId);

        (await pending).Should().BeNull("la respuesta tardía es del INFO anterior");
    }

    [Fact]
    public async Task DeviceInfo_ReplyThatArrivesBeforeTheWait_IsNotLost()
    {
        var service = NewService();
        var infoCommand = await service.EnqueueCommandAsync("SN_FAST", "INFO");
        var (id, _, _) = await service.GetNextCommandAsync("SN_FAST");
        await service.NotifyDeviceInfoReceivedAsync("SN_FAST", Info("SN_FAST"), id);

        (await service.WaitForDeviceInfoAsync(infoCommand, TimeSpan.FromSeconds(2))).Should().Be(Info("SN_FAST"));
    }

    [Fact]
    public async Task GetNextCommand_GeneratesUniqueNonCollidingIdsInRange10000To99999_H20()
    {
        var service = NewService();
        var sn = "SNTEST100";
        for (int i = 0; i < 20; i++)
        {
            await service.EnqueueCommandAsync(sn, $"DATA QUERY ATTLOG {i}");
        }

        var commandIds = new HashSet<string>();
        for (int i = 0; i < 20; i++)
        {
            var (cmdId, cmdText, _) = await service.GetNextCommandAsync(sn);
            cmdText.Should().Be($"DATA QUERY ATTLOG {i}", "se entregan en el orden en que se encolaron");
            int.TryParse(cmdId, out var numId).Should().BeTrue();
            numId.Should().BeInRange(10000, 99999);
            commandIds.Add(cmdId!);
        }

        commandIds.Should().HaveCount(20);
        (await service.GetNextCommandAsync(sn)).Command.Should().BeNull();
    }

    [Fact]
    public async Task ACommandEnqueuedByOneProcess_IsDeliveredByAnother_Once()
    {
        // Cada instancia hace de un proceso (WPF y la web): sólo comparten la base.
        var wpf = NewService();
        var web = NewService();

        await wpf.EnqueueCommandAsync("SN_SHARED", "DATA QUERY tablename=user,fields=*");

        (await web.HasPendingCommandsAsync("SN_SHARED")).Should().BeTrue();
        (await web.GetNextCommandAsync("SN_SHARED")).Command.Should().Be("DATA QUERY tablename=user,fields=*");
        (await wpf.GetNextCommandAsync("SN_SHARED")).Command.Should().BeNull("ya se entregó");
        (await web.HasPendingCommandsAsync("SN_SHARED")).Should().BeFalse();
    }

    [Fact]
    public void AdmsCommand_DestructiveCommand_CannotRetryAutomatically_H20()
    {
        var destructiveCmd = AdmsCommand.Create("SN123", "10001", "CLEAR LOG");
        var nonDestructiveCmd = AdmsCommand.Create("SN123", "10002", "DATA QUERY ATTLOG");

        destructiveCmd.MarkAsFailed(-1, "Error de hardware");
        nonDestructiveCmd.MarkAsFailed(-1, "Timeout transitorio");

        destructiveCmd.IsDestructive.Should().BeTrue("CLEAR LOG es una operación destructiva");
        destructiveCmd.CanRetryAutomatically().Should().BeFalse("operaciones destructivas nunca deben reintentarse automáticamente");

        nonDestructiveCmd.IsDestructive.Should().BeFalse();
        nonDestructiveCmd.CanRetryAutomatically().Should().BeTrue("consultas de datos son idempotentes y pueden reintentarse");
    }

    [Fact]
    public async Task AcknowledgeCommandExecutionAsync_WhenDestructiveCommandFails_DoesNotRequeue()
    {
        var service = NewService();
        var sn = "SN_CLEAR_TEST";
        await service.EnqueueCommandAsync(sn, "CLEAR LOG");

        var (cmdId, _, _) = await service.GetNextCommandAsync(sn);
        cmdId.Should().NotBeNull();

        await service.AcknowledgeCommandExecutionAsync(sn, cmdId!, -1, "Error reloj");

        (await service.HasPendingCommandsAsync(sn)).Should().BeFalse();
    }

    [Fact]
    public async Task AcknowledgeCommandExecutionAsync_WhenQueryFails_RequeuesIt()
    {
        var service = NewService();
        await service.EnqueueCommandAsync("SN_RETRY", "DATA QUERY tablename=user,fields=*");
        var (cmdId, _, _) = await service.GetNextCommandAsync("SN_RETRY");

        await service.AcknowledgeCommandExecutionAsync("SN_RETRY", cmdId!, -1, "Timeout");

        (await service.GetNextCommandAsync("SN_RETRY")).Command.Should().Be("DATA QUERY tablename=user,fields=*");
    }

    [Fact]
    public async Task OnStartup_ACommandSentLongAgoWithoutAck_Expires_AndIsNotSentAgain()
    {
        var service = NewService();
        await service.EnqueueCommandAsync("SN_STALE", "CLEAR LOG");
        await service.GetNextCommandAsync("SN_STALE");
        await using (var context = new AttendanceDbContext(_dbOptions, _publisherMock.Object))
        {
            var sent = await context.AdmsCommands.SingleAsync(c => c.SerialNumber == "SN_STALE");
            context.Entry(sent).Property(c => c.SentAtUtc).CurrentValue = DateTime.UtcNow - AdmsCommandService.SentCommandTtl - TimeSpan.FromMinutes(1);
            await context.SaveChangesAsync();
        }

        await service.ExpireStaleSentCommandsAsync();

        await using (var context = new AttendanceDbContext(_dbOptions, _publisherMock.Object))
        {
            (await context.AdmsCommands.SingleAsync(c => c.SerialNumber == "SN_STALE")).Status.Should().Be(AdmsCommandStatus.Expired);
        }
        (await service.HasPendingCommandsAsync("SN_STALE")).Should().BeFalse();
    }

    [Fact]
    public async Task OnStartup_OldUnfinishedDownloads_AreClosed_UnlessTheyStillAwaitACommand()
    {
        var device = AttendanceSystem.Domain.ValueObjects.DeviceId.From("dev-1");
        var old = DateTime.UtcNow - AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLog.AbandonAfter - TimeSpan.FromMinutes(1);
        var abandoned = AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLog.Create(device, AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadType.Automatic);
        var awaiting = AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLog.Create(device, AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadType.Manual);
        var recent = AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLog.Create(device, AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadType.Manual);
        await using (var context = new AttendanceDbContext(_dbOptions, _publisherMock.Object))
        {
            context.DownloadLogs.AddRange(abandoned, awaiting, recent);
            context.Entry(abandoned).Property(l => l.StartedAt).CurrentValue = old;
            context.Entry(awaiting).Property(l => l.StartedAt).CurrentValue = old;
            // Reloj apagado: su consulta sigue pendiente, la descarga todavía puede terminar.
            context.AdmsCommands.Add(AdmsCommand.Create("SN_OFFLINE", "0",
                "DATA QUERY ATTLOG StartTime=2026-11-01 00:00:00\tEndTime=2026-11-05 00:00:00", awaiting.Id.Value));
            await context.SaveChangesAsync();
        }

        // El arranque del servicio ya hace esta limpieza; se llama de nuevo para esperar su resultado.
        var service = NewService();
        await service.CloseAbandonedDownloadLogsAsync();

        await using (var context = new AttendanceDbContext(_dbOptions, _publisherMock.Object))
        {
            var closed = await context.DownloadLogs.SingleAsync(l => l.Id == abandoned.Id);
            closed.CompletedAt.Should().NotBeNull();
            closed.IsSuccessful.Should().BeFalse();
            closed.ErrorMessage.Should().Be(AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLog.AbandonedMessage);
            (await context.DownloadLogs.SingleAsync(l => l.Id == awaiting.Id)).CompletedAt.Should().BeNull();
            (await context.DownloadLogs.SingleAsync(l => l.Id == recent.Id)).CompletedAt.Should().BeNull();
        }
    }

    [Fact]
    public void ADownloadStopsBeingInProgress_OnceItIsTooOld()
    {
        var log = AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLog.Create(
            AttendanceSystem.Domain.ValueObjects.DeviceId.From("dev-1"),
            AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadType.Manual);

        log.IsInProgressAt(log.StartedAt.AddMinutes(5)).Should().BeTrue();
        log.IsInProgressAt(log.StartedAt + AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLog.AbandonAfter).Should().BeFalse();
        log.MarkAsSuccessful(1, 1);
        log.IsInProgressAt(log.StartedAt.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadSession_AccumulatesRecordsAndNotifiesOnCompletion()
    {
        var service = NewService();
        var sn = "SN_DOWNLOAD_SESSION";
        var logId = Guid.NewGuid();

        await service.EnqueueCommandAsync(sn, "DATA QUERY tablename=ATTLOG", logId);
        var (cmdId, _, returnedLogId) = await service.GetNextCommandAsync(sn);
        returnedLogId.Should().Be(logId);

        service.GetActiveDownloadLogId(sn, cmdId).Should().Be(logId);

        service.RecordDownloadProgress(sn, cmdId, totalReceived: 10, newInserted: 8);
        service.RecordDownloadProgress(sn, cmdId, totalReceived: 15, newInserted: 12);

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

        service.GetActiveDownloadLogId(sn, cmdId).Should().BeNull();
    }
}
