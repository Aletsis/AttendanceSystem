using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.Infrastructure.Tests.Services;

public class BackupServiceTests
{
    private readonly Mock<ISystemConfigurationRepository> _systemConfigRepoMock;
    private readonly Dictionary<string, string?> _configDict;

    public BackupServiceTests()
    {
        _systemConfigRepoMock = new Mock<ISystemConfigurationRepository>();
        _configDict = new Dictionary<string, string?>
        {
            { "ConnectionStrings:AttendanceDb", "Host=127.0.0.1;Port=5432;Database=attendance_test;Username=postgres;Password=secret" },
            { "Backup:DumpStallTimeoutSeconds", "600" },
            { "Backup:RestoreStallTimeoutSeconds", "900" },
            { "Backup:KeepLastNBackups", "10" }
        };
    }

    private BackupService CreateService(Dictionary<string, string?>? customConfig = null)
    {
        var dict = new Dictionary<string, string?>(_configDict);
        if (customConfig != null)
        {
            foreach (var kvp in customConfig)
            {
                dict[kvp.Key] = kvp.Value;
            }
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        return new BackupService(
            config,
            NullLogger<BackupService>.Instance,
            _systemConfigRepoMock.Object);
    }


    [Fact]
    public void GetDatabaseConnectionInfo_ShouldParseConnectionStringCorrectly()
    {
        var service = CreateService();

        var info = service.GetDatabaseConnectionInfo();

        info.Should().NotBeNull();
        info.Host.Should().Be("127.0.0.1");
        info.Port.Should().Be(5432);
        info.Database.Should().Be("attendance_test");
        info.Username.Should().Be("postgres");
        info.Engine.Should().Be("PostgreSQL");
    }

    [Fact]
    public void FindPgToolPath_ShouldUseConfiguredBinPath_WhenFileExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pg_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var toolFileName = isWindows ? "pg_dump.exe" : "pg_dump";
            var fakeToolPath = Path.Combine(tempDir, toolFileName);
            File.WriteAllText(fakeToolPath, "echo test");

            var customConfig = new Dictionary<string, string?>
            {
                { "Backup:PostgresBinPath", tempDir }
            };

            var service = CreateService(customConfig);
            var resolvedPath = service.FindPgToolPath("pg_dump");

            resolvedPath.Should().Be(fakeToolPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void FindPgToolPath_OnLinux_ShouldNotAppendExe()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"pg_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var fakeToolPath = Path.Combine(tempDir, "pg_restore");
            File.WriteAllText(fakeToolPath, "echo test");

            var customConfig = new Dictionary<string, string?>
            {
                { "Backup:PostgresBinPath", tempDir }
            };

            var service = CreateService(customConfig);
            var resolvedPath = service.FindPgToolPath("pg_restore");


            resolvedPath.Should().EndWith("pg_restore");
            resolvedPath.Should().NotEndWith(".exe");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task PruneOldBackupsAsync_ShouldDeleteFilesExceedingRetentionLimit()
    {
        var tempBackupDir = Path.Combine(Path.GetTempPath(), $"backup_prune_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBackupDir);

        try
        {
            var configEntity = AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate.SystemConfiguration.CreateDefault();
            configEntity.UpdateBackupDirectory(tempBackupDir);
            _systemConfigRepoMock.Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(configEntity);

            // Crear 5 archivos de respaldo simulados con fechas distintas
            var file1 = Path.Combine(tempBackupDir, "Full_Backup_20260101_100000.zip");
            var file2 = Path.Combine(tempBackupDir, "Full_Backup_20260102_100000.zip");
            var file3 = Path.Combine(tempBackupDir, "Full_Backup_20260103_100000.zip");
            var file4 = Path.Combine(tempBackupDir, "DB_Backup_20260104_100000.backup");
            var file5 = Path.Combine(tempBackupDir, "DB_Backup_20260105_100000.backup");

            File.WriteAllText(file1, "dummy 1");
            File.SetCreationTime(file1, new DateTime(2026, 1, 1));
            File.WriteAllText(file2, "dummy 2");
            File.SetCreationTime(file2, new DateTime(2026, 1, 2));
            File.WriteAllText(file3, "dummy 3");
            File.SetCreationTime(file3, new DateTime(2026, 1, 3));
            File.WriteAllText(file4, "dummy 4");
            File.SetCreationTime(file4, new DateTime(2026, 1, 4));
            File.WriteAllText(file5, "dummy 5");
            File.SetCreationTime(file5, new DateTime(2026, 1, 5));

            var service = CreateService();

            // Purgar manteniendo solo los 3 más recientes
            var deletedCount = await service.PruneOldBackupsAsync(keepCount: 3);

            deletedCount.Should().Be(2);

            // Los dos más viejos deberían haber sido eliminados
            File.Exists(file1).Should().BeFalse();
            File.Exists(file2).Should().BeFalse();

            // Los tres más recientes deben conservarse
            File.Exists(file3).Should().BeTrue();
            File.Exists(file4).Should().BeTrue();
            File.Exists(file5).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempBackupDir))
            {
                Directory.Delete(tempBackupDir, true);
            }
        }
    }
}
