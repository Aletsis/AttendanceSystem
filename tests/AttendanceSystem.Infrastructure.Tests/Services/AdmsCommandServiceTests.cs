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
}
