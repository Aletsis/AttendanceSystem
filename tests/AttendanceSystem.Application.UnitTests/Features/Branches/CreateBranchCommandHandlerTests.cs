using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Branches.Commands.CreateBranch;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Branches;

public class CreateBranchCommandHandlerTests
{
    private readonly Mock<IBranchRepository> _branchRepositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly CreateBranchCommandHandler _handler;

    public CreateBranchCommandHandlerTests()
    {
        _branchRepositoryMock = new Mock<IBranchRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();

        _handler = new CreateBranchCommandHandler(
            _branchRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task Handle_WhenValidParameters_ShouldSaveBranchAndReturnId()
    {
        // Arrange
        var command = new CreateBranchCommand(
            Code: "A01",
            Name: "Sucursal Centro",
            Address: "Av. Principal 100",
            IsExternal: false,
            ExternalHost: null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        _branchRepositoryMock.Verify(r => r.AddAsync(It.Is<Branch>(b =>
            b.Code == "A01" &&
            b.Name == "Sucursal Centro"), It.IsAny<CancellationToken>()), Times.Once);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenInvalidCode_ShouldReturnFailure()
    {
        // Arrange
        var command = new CreateBranchCommand(
            Code: "INVALID_CODE",
            Name: "Sucursal",
            Address: null,
            IsExternal: false,
            ExternalHost: null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        _branchRepositoryMock.Verify(r => r.AddAsync(It.IsAny<Branch>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
