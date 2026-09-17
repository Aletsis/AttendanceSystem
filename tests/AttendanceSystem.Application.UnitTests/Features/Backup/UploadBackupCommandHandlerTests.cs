using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Backup.Commands;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.IO;
using System.Text;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Backup;

public class UploadBackupCommandHandlerTests
{
    private readonly Mock<IBackupService> _backupServiceMock;
    private readonly Mock<ILogger<UploadBackupCommandHandler>> _loggerMock;
    private readonly UploadBackupCommandHandler _handler;

    public UploadBackupCommandHandlerTests()
    {
        _backupServiceMock = new Mock<IBackupService>();
        _loggerMock = new Mock<ILogger<UploadBackupCommandHandler>>();

        _handler = new UploadBackupCommandHandler(
            _backupServiceMock.Object,
            _loggerMock.Object);
    }

    [Theory]
    [InlineData("backup_2026.zip")]
    [InlineData("database_dump.backup")]
    [InlineData("db_export.bak")]
    public async Task Handle_WhenValidExtension_ShouldCallUploadBackupAsync(string fileName)
    {
        // Arrange
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test-content"));
        var command = new UploadBackupCommand(fileName, stream);
        var expectedResult = new BackupResultDto
        {
            Success = true,
            BackupFilePath = $"/backups/{fileName}",
            Message = "Respaldo cargado exitosamente."
        };

        _backupServiceMock
            .Setup(s => s.UploadBackupAsync(fileName, stream, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.BackupFilePath.Should().Be($"/backups/{fileName}");
        _backupServiceMock.Verify(s => s.UploadBackupAsync(fileName, stream, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("backup.exe")]
    [InlineData("script.sh")]
    [InlineData("malicious.php")]
    [InlineData("document.pdf")]
    public async Task Handle_WhenInvalidExtension_ShouldReturnFailureWithoutCallingService(string fileName)
    {
        // Arrange
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test-content"));
        var command = new UploadBackupCommand(fileName, stream);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Formato de archivo no soportado");
        _backupServiceMock.Verify(s => s.UploadBackupAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEmptyFileName_ShouldReturnFailure()
    {
        // Arrange
        using var stream = new MemoryStream();
        var command = new UploadBackupCommand("", stream);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("requeridos");
    }

    [Fact]
    public async Task Handle_WhenServiceThrowsException_ShouldReturnFailureGracefully()
    {
        // Arrange
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test-content"));
        var command = new UploadBackupCommand("backup.zip", stream);

        _backupServiceMock
            .Setup(s => s.UploadBackupAsync("backup.zip", stream, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk read/write failure"));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Disk read/write failure");
    }
}
