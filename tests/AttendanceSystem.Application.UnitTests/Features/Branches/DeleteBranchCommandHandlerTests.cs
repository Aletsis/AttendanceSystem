using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Branches.Commands.DeleteBranch;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Branches;

public class DeleteBranchCommandHandlerTests
{
    private readonly Mock<IBranchRepository> _branches = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IExternalEmployeeRepository> _externalEmployees = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Branch _branch = Branch.Create("B01", "Centro", null, isExternal: true, externalHost: "https://centro.example");
    private readonly DeleteBranchCommandHandler _handler;

    public DeleteBranchCommandHandlerTests()
    {
        _branches.Setup(r => r.GetByIdAsync(_branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_branch);
        _employees.Setup(r => r.GetByBranchAsync(_branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee>());
        _externalEmployees.Setup(r => r.GetByBranchAsync(_branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ExternalEmployee>());
        _handler = new DeleteBranchCommandHandler(_branches.Object, _employees.Object, _externalEmployees.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_WhenBranchIsUnused_DeletesIt()
    {
        var result = await _handler.Handle(new DeleteBranchCommand(_branch.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _branches.Verify(r => r.Delete(_branch), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenBranchHasExternalEmployees_RejectsAndKeepsIt()
    {
        _externalEmployees.Setup(r => r.GetByBranchAsync(_branch.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalEmployee> { ExternalEmployee.Create(_branch.Id, "101", "Ana", "López") });

        var result = await _handler.Handle(new DeleteBranchCommand(_branch.Id.Value), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("1 empleado(s) externo(s)");
        _branches.Verify(r => r.Delete(It.IsAny<Branch>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_Fails()
    {
        var result = await _handler.Handle(new DeleteBranchCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        _branches.Verify(r => r.Delete(It.IsAny<Branch>()), Times.Never);
    }
}
