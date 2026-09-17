using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Backup.Commands;
using AttendanceSystem.Domain.Aggregates.SystemConfigurationAggregate;
using AttendanceSystem.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Backup;

public class UpdateBackupDirectoryCommandHandlerTests
{
    private readonly Mock<ISystemConfigurationRepository> _repositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ILogger<UpdateBackupDirectoryCommandHandler>> _loggerMock;
    private readonly UpdateBackupDirectoryCommandHandler _handler;

    public UpdateBackupDirectoryCommandHandlerTests()
    {
        _repositoryMock = new Mock<ISystemConfigurationRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _loggerMock = new Mock<ILogger<UpdateBackupDirectoryCommandHandler>>();

        _handler = new UpdateBackupDirectoryCommandHandler(
            _repositoryMock.Object,
            _unitOfWorkMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenValidDirectoryProvided_ShouldUpdateConfigurationAndReturnSuccess()
    {
        // Arrange
        var config = SystemConfiguration.CreateDefault();
        _repositoryMock
            .Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var newPath = "/var/backups/attendance";
        var command = new UpdateBackupDirectoryCommand(newPath);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(newPath);
        config.BackupDirectory.Should().Be(newPath);

        _repositoryMock.Verify(r => r.Update(config), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmptyDirectoryProvided_ShouldReturnFailure()
    {
        // Arrange
        var command = new UpdateBackupDirectoryCommand("   ");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("no puede estar vacío");
        _repositoryMock.Verify(r => r.Update(It.IsAny<SystemConfiguration>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenConfigurationDoesNotExist_ShouldCreateDefaultAndSetDirectory()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((SystemConfiguration?)null);

        var newPath = "D:\\AttendanceBackups";
        var command = new UpdateBackupDirectoryCommand(newPath);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(newPath);

        _repositoryMock.Verify(r => r.Add(It.Is<SystemConfiguration>(c => c.BackupDirectory == newPath)), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
