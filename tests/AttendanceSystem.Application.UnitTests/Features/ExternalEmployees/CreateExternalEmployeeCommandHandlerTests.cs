using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.ExternalEmployees.Commands;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.ExternalEmployees;

public class CreateExternalEmployeeCommandHandlerTests
{
    private readonly Mock<IExternalEmployeeRepository> _externalEmployeeRepoMock;
    private readonly Mock<IBranchRepository> _branchRepoMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ILogger<CreateExternalEmployeeCommandHandler>> _loggerMock;
    private readonly CreateExternalEmployeeCommandHandler _handler;

    private readonly Branch _externalBranch;
    private readonly Branch _internalBranch;

    public CreateExternalEmployeeCommandHandlerTests()
    {
        _externalEmployeeRepoMock = new Mock<IExternalEmployeeRepository>();
        _branchRepoMock = new Mock<IBranchRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _loggerMock = new Mock<ILogger<CreateExternalEmployeeCommandHandler>>();

        _handler = new CreateExternalEmployeeCommandHandler(
            _externalEmployeeRepoMock.Object,
            _branchRepoMock.Object,
            _unitOfWorkMock.Object,
            _loggerMock.Object);

        _externalBranch = Branch.Create("A01", "Sucursal Norte", "Av 123", isExternal: true, externalHost: "192.168.1.100");
        _internalBranch = Branch.Create("B01", "Matriz Centro", "Calle 1", isExternal: false);
    }

    [Fact]
    public async Task Handle_ValidRequest_ShouldCreateAndReturnSuccess()
    {
        // Arrange
        _branchRepoMock.Setup(r => r.GetByIdAsync(_externalBranch.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_externalBranch);

        _externalEmployeeRepoMock.Setup(r => r.ExistsInBranchAsync(_externalBranch.Id, "101", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var command = new CreateExternalEmployeeCommand(
            _externalBranch.Id.Value.ToString(),
            "101",
            "Juan",
            "Perez",
            "juan@externa.com",
            "555-1234",
            "Cajero",
            "Operaciones",
            EmployeeStatus.Alta);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.EmployeeNumber.Should().Be("101");
        result.Value.FullName.Should().Be("Juan Perez");
        result.Value.BranchCode.Should().Be("A01");
        _externalEmployeeRepoMock.Verify(r => r.Add(It.IsAny<ExternalEmployee>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenBranchIsInternal_ShouldFail()
    {
        // Arrange
        _branchRepoMock.Setup(r => r.GetByIdAsync(_internalBranch.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_internalBranch);

        var command = new CreateExternalEmployeeCommand(
            _internalBranch.Id.Value.ToString(),
            "101",
            "Juan",
            "Perez");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("no está configurada como sucursal externa");
        _externalEmployeeRepoMock.Verify(r => r.Add(It.IsAny<ExternalEmployee>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEmployeeNumberAlreadyExistsInSameBranch_ShouldFail()
    {
        // Arrange
        _branchRepoMock.Setup(r => r.GetByIdAsync(_externalBranch.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_externalBranch);

        _externalEmployeeRepoMock.Setup(r => r.ExistsInBranchAsync(_externalBranch.Id, "101", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new CreateExternalEmployeeCommand(
            _externalBranch.Id.Value.ToString(),
            "101",
            "Juan",
            "Perez");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Ya existe un empleado con el número '101'");
        _externalEmployeeRepoMock.Verify(r => r.Add(It.IsAny<ExternalEmployee>()), Times.Never);
    }
}
