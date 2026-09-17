using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Backup.Commands;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Backup;

public class CreateBackupCommandHandlerTests
{
    private readonly Mock<IBackupService> _backupServiceMock;
    private readonly Mock<ILogger<CreateBackupCommandHandler>> _loggerMock;
    private readonly CreateBackupCommandHandler _handler;

    public CreateBackupCommandHandlerTests()
    {
        _backupServiceMock = new Mock<IBackupService>();
        _loggerMock = new Mock<ILogger<CreateBackupCommandHandler>>();

        _handler = new CreateBackupCommandHandler(
            _backupServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenFullBackupRequested_ShouldCallCreateFullBackupAsync()
    {
        // Arrange
        var command = new CreateBackupCommand("Full", "Respaldo Completo Semanal");
        var expectedResult = new BackupResultDto
        {
            Success = true,
            BackupFilePath = "/backups/backup_full_2026.zip",
            Message = "Respaldo creado"
        };

        _backupServiceMock
            .Setup(s => s.CreateFullBackupAsync("Respaldo Completo Semanal", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.BackupFilePath.Should().Be("/backups/backup_full_2026.zip");
        _backupServiceMock.Verify(s => s.CreateFullBackupAsync("Respaldo Completo Semanal", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenDatabaseOnlyRequested_ShouldCallCreateDatabaseBackupAsync()
    {
        // Arrange
        var command = new CreateBackupCommand("DatabaseOnly", "Respaldo DB");
        var expectedResult = new BackupResultDto
        {
            Success = true,
            BackupFilePath = "/backups/backup_db.sql",
            Message = "Respaldo DB creado"
        };

        _backupServiceMock
            .Setup(s => s.CreateDatabaseBackupAsync("Respaldo DB", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _backupServiceMock.Verify(s => s.CreateDatabaseBackupAsync("Respaldo DB", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenInvalidBackupType_ShouldReturnFailure()
    {
        // Arrange
        var command = new CreateBackupCommand("TipoInvalido");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Tipo de respaldo no válido");
        _backupServiceMock.Verify(s => s.CreateFullBackupAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenServiceThrowsException_ShouldReturnFailureGracefully()
    {
        // Arrange
        var command = new CreateBackupCommand("Full");

        _backupServiceMock
            .Setup(s => s.CreateFullBackupAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Disk is full"));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Disk is full");
    }
}
