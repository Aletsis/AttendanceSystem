using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Positions.Queries.GetPositions;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Blazor.Server.Components.Pages;
using AttendanceSystem.Blazor.UnitTests.Common;
using MudBlazor;

namespace AttendanceSystem.Blazor.UnitTests.Pages;

public class OrganizationPageTests : BlazorTestBase
{
    [Fact]
    public void BranchesPage_ShouldRenderTableWithBranches()
    {
        var branches = new List<BranchDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Sucursal Norte", Code = "SN01", Address = "Av. Norte 123", IsExternal = false },
            new() { Id = Guid.NewGuid(), Name = "Sucursal Sur", Code = "SS02", Address = "Av. Sur 456", IsExternal = true }
        };

        SenderMock
            .Setup(s => s.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(branches));

        var cut = RenderComponent<Branches>();

        cut.Markup.Should().Contain("Sucursales");
        cut.Markup.Should().Contain("Sucursal Norte");
        cut.Markup.Should().Contain("Sucursal Sur");
        cut.FindComponents<MudTable<BranchDto>>().Should().NotBeEmpty();
    }

    [Fact]
    public void DepartmentsPage_ShouldRenderTableWithDepartments()
    {
        var departments = new List<DepartmentDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Recursos Humanos", Description = "Gestión de personal" },
            new() { Id = Guid.NewGuid(), Name = "Tecnología", Description = "TI y Desarrollo" }
        };

        SenderMock
            .Setup(s => s.Send(It.IsAny<GetDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DepartmentDto>>.Success(departments));

        var cut = RenderComponent<Departments>();

        cut.Markup.Should().Contain("Departamentos");
        cut.Markup.Should().Contain("Recursos Humanos");
        cut.Markup.Should().Contain("Tecnología");
    }

    [Fact]
    public void PositionsPage_ShouldRenderTableWithPositions()
    {
        var positions = new List<PositionDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Gerente de Operaciones", Description = "Liderazgo de operaciones" },
            new() { Id = Guid.NewGuid(), Name = "Analista de Sistemas", Description = "Soporte y análisis" }
        };

        SenderMock
            .Setup(s => s.Send(It.IsAny<GetPositionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<PositionDto>>.Success(positions));

        var cut = RenderComponent<Positions>();

        cut.Markup.Should().Contain("Puestos");
        cut.Markup.Should().Contain("Gerente de Operaciones");
        cut.Markup.Should().Contain("Analista de Sistemas");
    }

    [Fact]
    public void EmployeesPage_ShouldRenderFiltersAndActions()
    {
        var branches = new List<BranchDto> { new() { Id = Guid.NewGuid(), Name = "Sucursal Central" } };
        var depts = new List<DepartmentDto> { new() { Id = Guid.NewGuid(), Name = "Finanzas" } };

        SenderMock
            .Setup(s => s.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(branches));

        SenderMock
            .Setup(s => s.Send(It.IsAny<GetDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DepartmentDto>>.Success(depts));

        var cut = RenderComponent<Employees>();

        cut.Markup.Should().Contain("Empleados");
        cut.Markup.Should().Contain("Nuevo Empleado");
        cut.Markup.Should().Contain("Importar");
        cut.Markup.Should().Contain("Exportar");
    }
}
