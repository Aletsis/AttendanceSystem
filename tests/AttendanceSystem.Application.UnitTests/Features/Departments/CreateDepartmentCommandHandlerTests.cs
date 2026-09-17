using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Departments.Commands.CreateDepartment;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Departments;

public class CreateDepartmentCommandHandlerTests
{
    private readonly Mock<IDepartmentRepository> _departmentRepoMock;
    private readonly Mock<IPositionRepository> _positionRepoMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly CreateDepartmentCommandHandler _handler;

    public CreateDepartmentCommandHandlerTests()
    {
        _departmentRepoMock = new Mock<IDepartmentRepository>();
        _positionRepoMock = new Mock<IPositionRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();

        _handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object,
            _positionRepoMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task Handle_WhenValidParameters_ShouldSaveDepartmentAndAssignPositions()
    {
        // Arrange
        var position = Position.Create("Contador", "Auditor", 20000m);
        var command = new CreateDepartmentCommand(
            Name: "Finanzas",
            Description: "Dpto Contable",
            PositionIds: new List<Guid> { position.Id.Value });

        _positionRepoMock
            .Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        _departmentRepoMock.Verify(r => r.AddAsync(It.Is<Department>(d =>
            d.Name == "Finanzas" &&
            d.Description == "Dpto Contable" &&
            d.Positions.Count == 1), It.IsAny<CancellationToken>()), Times.Once);

        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmptyName_ShouldReturnFailure()
    {
        // Arrange
        var command = new CreateDepartmentCommand(string.Empty, "Desc");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        _departmentRepoMock.Verify(r => r.AddAsync(It.IsAny<Department>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
