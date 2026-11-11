using AttendanceSystem.Application.Features.ExternalEmployees.Queries;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.ExternalEmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

namespace AttendanceSystem.Application.UnitTests.Features.ExternalEmployees;

public class GetExternalEmployeesQueryHandlerTests
{
    [Theory]
    [InlineData("juan perez")]
    [InlineData("PEREZ JUAN")]
    [InlineData("ext-01")]
    [InlineData("tecnico")]
    [InlineData("produccion")]
    public async Task Handle_ShouldSearchIgnoringCaseAccentsAndWordOrder_AndMatchFullName(string searchTerm)
    {
        var branch = Branch.Create("E01", "Planta Norte", null, isExternal: true);
        var target = ExternalEmployee.Create(
            branch.Id,
            "EXT-01",
            "JUAN CARLOS",
            "PÉREZ",
            position: "Técnico",
            department: "Producción",
            cardNumber: "CARD-555");
        var other = ExternalEmployee.Create(
            branch.Id,
            "EXT-02",
            "MARÍA",
            "LÓPEZ",
            position: "Analista",
            department: "Calidad");

        var extRepo = new Mock<IExternalEmployeeRepository>();
        extRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { target, other });

        var branchRepo = new Mock<IBranchRepository>();
        branchRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { branch });

        var handler = new GetExternalEmployeesQueryHandler(extRepo.Object, branchRepo.Object);

        var result = await handler.Handle(new GetExternalEmployeesQuery(SearchTerm: searchTerm), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => e.EmployeeNumber == "EXT-01");
    }

    [Fact]
    public async Task Handle_ShouldNotMatchCardNumber()
    {
        var branch = Branch.Create("E01", "Planta Norte", null, isExternal: true);
        var target = ExternalEmployee.Create(
            branch.Id,
            "EXT-01",
            "JUAN",
            "PÉREZ",
            cardNumber: "CARD-555");

        var extRepo = new Mock<IExternalEmployeeRepository>();
        extRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { target });

        var branchRepo = new Mock<IBranchRepository>();
        branchRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { branch });

        var handler = new GetExternalEmployeesQueryHandler(extRepo.Object, branchRepo.Object);

        var result = await handler.Handle(new GetExternalEmployeesQuery(SearchTerm: "CARD-555"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
