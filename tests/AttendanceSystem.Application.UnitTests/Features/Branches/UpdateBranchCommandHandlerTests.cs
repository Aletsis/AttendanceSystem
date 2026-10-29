using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Branches.Commands.UpdateBranch;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.Branches;

public class UpdateBranchCommandHandlerTests
{
    private readonly Mock<IBranchRepository> _branches = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IExternalEmployeeRepository> _externalEmployees = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Branch _branch = Branch.Create("B01", "Remota", null, isExternal: true);
    private readonly UpdateBranchCommandHandler _handler;

    public UpdateBranchCommandHandlerTests()
    {
        _branches.Setup(r => r.GetByIdAsync(_branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_branch);
        _employees.Setup(r => r.GetByBranchAsync(_branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Employee>());
        _externalEmployees.Setup(r => r.GetByBranchAsync(_branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ExternalEmployee>());
        _handler = new UpdateBranchCommandHandler(_branches.Object, _employees.Object, _externalEmployees.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task Handle_WhenCodeChanges_FailsAndKeepsTheCode()
    {
        var result = await _handler.Handle(new UpdateBranchCommand(_branch.Id.Value, "B02", "Remota", null, true, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("no se puede cambiar");
        _branch.Code.Should().Be("B01");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUnmarkingExternalWithExternalEmployees_Fails()
    {
        _externalEmployees.Setup(r => r.GetByBranchAsync(_branch.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalEmployee> { ExternalEmployee.Create(_branch.Id, "123", "Ana", "López") });

        var result = await _handler.Handle(new UpdateBranchCommand(_branch.Id.Value, "B01", "Remota", null, false, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("1 empleado(s) externo(s)");
        _branch.IsExternal.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenTogglingAnUnusedBranch_Succeeds()
    {
        var result = await _handler.Handle(new UpdateBranchCommand(_branch.Id.Value, "B01", "Ahora local", null, false, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _branch.IsExternal.Should().BeFalse();
        _branch.Name.Should().Be("Ahora local");
    }
}
