using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Backup.Commands;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Backup;

public class RestoreBackupCommandHandlerTests
{
    private readonly Mock<IBackupService> _backupServiceMock;
    private readonly Mock<ILogger<RestoreBackupCommandHandler>> _loggerMock;
    private readonly RestoreBackupCommandHandler _handler;

    public RestoreBackupCommandHandlerTests()
    {
        _backupServiceMock = new Mock<IBackupService>();
        _loggerMock = new Mock<ILogger<RestoreBackupCommandHandler>>();

        _handler = new RestoreBackupCommandHandler(
            _backupServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenBackupFileIsInvalid_ShouldReturnFailureWithoutRestoring()
    {
        // Arrange
        var command = new RestoreBackupCommand("/backups/corrupted.bak");

        _backupServiceMock
            .Setup(s => s.ValidateBackupAsync("/backups/corrupted.bak", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("no es válido o está corrupto");
        _backupServiceMock.Verify(s => s.RestoreBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenBackupFileIsValid_ShouldCallRestoreBackupAsync()
    {
        // Arrange
        var command = new RestoreBackupCommand("/backups/valid_backup.zip");

        _backupServiceMock
            .Setup(s => s.ValidateBackupAsync("/backups/valid_backup.zip", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _backupServiceMock
            .Setup(s => s.RestoreBackupAsync("/backups/valid_backup.zip", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RestoreResultDto { Success = true, Message = "Restaurado con éxito" });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().Be("Restaurado con éxito");
        _backupServiceMock.Verify(s => s.RestoreBackupAsync("/backups/valid_backup.zip", It.IsAny<CancellationToken>()), Times.Once);
    }
}
