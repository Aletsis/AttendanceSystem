using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Positions.Queries.GetPositions;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Blazor.Server.Components.Pages;
using AttendanceSystem.Blazor.UnitTests.Common;

namespace AttendanceSystem.Blazor.UnitTests.Pages;

public class PageSmokeTests : BlazorTestBase
{
    [Fact]
    public void BranchesPage_ShouldRenderWithoutException()
    {
        SenderMock
            .Setup(s => s.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(new List<BranchDto>()));

        var cut = RenderComponent<Branches>();
        cut.Markup.Should().Contain("Sucursales");
    }

    [Fact]
    public void DepartmentsPage_ShouldRenderWithoutException()
    {
        SenderMock
            .Setup(s => s.Send(It.IsAny<GetDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DepartmentDto>>.Success(new List<DepartmentDto>()));

        var cut = RenderComponent<Departments>();
        cut.Markup.Should().Contain("Departamentos");
    }

    [Fact]
    public void PositionsPage_ShouldRenderWithoutException()
    {
        SenderMock
            .Setup(s => s.Send(It.IsAny<GetPositionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<PositionDto>>.Success(new List<PositionDto>()));

        var cut = RenderComponent<Positions>();
        cut.Markup.Should().Contain("Puestos");
    }

    [Fact]
    public void EmployeesPage_ShouldRenderWithoutException()
    {
        SenderMock
            .Setup(s => s.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(new List<BranchDto>()));

        SenderMock
            .Setup(s => s.Send(It.IsAny<GetDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DepartmentDto>>.Success(new List<DepartmentDto>()));

        var cut = RenderComponent<Employees>();
        cut.Markup.Should().Contain("Empleados");
    }
}
