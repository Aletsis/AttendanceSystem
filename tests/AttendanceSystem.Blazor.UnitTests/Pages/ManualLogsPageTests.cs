using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Application.Features.Employees;
using AttendanceSystem.Application.Features.Employees.Queries;
using AttendanceSystem.Blazor.Server.Components.Pages;
using AttendanceSystem.Blazor.UnitTests.Common;
using MudBlazor;

namespace AttendanceSystem.Blazor.UnitTests.Pages;

public class ManualLogsPageTests : BlazorTestBase
{
    [Fact]
    public void ManualLogs_ShouldRenderSuccessfully_AndNotThrowInvalidCastException()
    {
        // Arrange: Setup mock data for branches and employees
        var branchId = Guid.NewGuid();
        var branches = new List<BranchDto>
        {
            new() { Id = branchId, Name = "Sucursal Principal", Code = "SP01" }
        };

        var employees = new List<EmployeeDto>
        {
            new() { Id = "EMP001", FirstName = "Juan", LastName = "Pérez", FullName = "Juan Pérez", BranchId = branchId },
            new() { Id = "EMP002", FirstName = "Ana", LastName = "Gómez", FullName = "Ana Gómez", BranchId = branchId }
        };

        MediatorMock
            .Setup(m => m.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(branches));

        MediatorMock
            .Setup(m => m.Send(It.IsAny<GetAllEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<EmployeeDto>>.Success(employees));

        // Act: Render the ManualLogs razor component
        // This exercises BuildRenderTree and the MudSelect with MultiSelectionTextFunc
        var cut = RenderComponent<ManualLogs>();

        // Assert: Verify component rendered properly without crashing
        cut.Markup.Should().Contain("Registro Manual de Logs");
        cut.Markup.Should().Contain("Asignación Masiva");
        cut.Markup.Should().Contain("Importar Archivo");

        // Verify MudSelect components are present
        var selects = cut.FindComponents<MudSelect<string>>();
        selects.Should().NotBeEmpty();
    }
}
