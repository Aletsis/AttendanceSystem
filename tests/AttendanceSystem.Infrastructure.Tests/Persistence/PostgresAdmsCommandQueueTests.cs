using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Persistence;

/// <summary>
/// La cola ADMS en PostgreSQL entre procesos: el cliente WPF encola y la web (o el receptor ADMS del WPF) entrega.
/// Cada <see cref="AdmsCommandService"/> hace de un proceso distinto; sólo comparten la base.
/// </summary>
public class PostgresAdmsCommandQueueTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public PostgresAdmsCommandQueueTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private AdmsCommandService NewProcess()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _fixture.CreateDbContext());
        var provider = services.BuildServiceProvider();
        return new AdmsCommandService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AdmsCommandService>.Instance);
    }

    [Fact]
    public async Task ConcurrentPolls_DeliverEachCommandExactlyOnce()
    {
        if (!_fixture.IsAvailable) return;
        var sn = $"SN_{Guid.NewGuid():N}"[..20];
        var wpf = NewProcess();
        for (int i = 0; i < 30; i++) await wpf.EnqueueCommandAsync(sn, $"DATA QUERY tablename=user,fields=* {i}");

        // Dos receptores (p. ej. dos workers de IIS) atienden a la vez peticiones del mismo reloj.
        var receivers = new[] { NewProcess(), NewProcess() };
        var delivered = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Task.WhenAll(receivers.Select(r => Task.Run(async () =>
        {
            while (true)
            {
                var (_, command, _) = await r.GetNextCommandAsync(sn);
                if (command == null) return;
                delivered.Add(command);
            }
        })));

        delivered.Should().HaveCount(30).And.OnlyHaveUniqueItems();
        (await wpf.HasPendingCommandsAsync(sn)).Should().BeFalse();
    }

    [Fact]
    public async Task DeviceInfoRequestedByOneProcess_IsAnsweredThroughAnother()
    {
        if (!_fixture.IsAvailable) return;
        var sn = $"SN_{Guid.NewGuid():N}"[..20];
        var wpf = NewProcess();
        var web = NewProcess();
        var info = new AttendanceSystem.Application.DTOs.DeviceInfoDto(sn, "MiniAC Plus", "1.2.3", "ZK", 42, 3, 0, 123, 3000, 100, 0, 150000, "2.4.1", "6.60", "00:17:61:12:34:56");

        var infoCommand = await wpf.EnqueueCommandAsync(sn, "INFO");
        var pending = wpf.WaitForDeviceInfoAsync(infoCommand, TimeSpan.FromSeconds(10));

        var (commandId, command, _) = await web.GetNextCommandAsync(sn);
        command.Should().Be("INFO");
        await web.AcknowledgeCommandExecutionAsync(sn, commandId!, 0);
        await web.NotifyDeviceInfoReceivedAsync(sn, info, commandId);

        (await pending).Should().Be(info);
        await using var context = _fixture.CreateDbContext();
        (await context.AdmsCommands.SingleAsync(c => c.Id == infoCommand)).CommandId.Should().Be(commandId);
    }
}
