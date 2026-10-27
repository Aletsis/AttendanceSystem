using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Branches.Queries.GetBranches;
using AttendanceSystem.Application.Features.Departments.Queries.GetDepartments;
using AttendanceSystem.Application.Features.Positions.Queries.GetPositions;
using AttendanceSystem.Application.Features.Roster.Queries.GetRosterEmployees;
using AttendanceSystem.Application.Features.Shifts.Queries.GetShifts;
using AttendanceSystem.Blazor.Server.Components.Dialogs;
using AttendanceSystem.Blazor.UnitTests.Common;
using AttendanceSystem.Domain.Enumerations;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace AttendanceSystem.Blazor.UnitTests.Dialogs;

/// <summary>
/// Editar a un empleado rotativo que ya tiene su esquema no debe regenerar su roster ni cambiar su horario o descansos
/// con los valores por omisión del editor oculto; sólo "Modificar esquema" lo hace.
/// </summary>
public class EmployeeDialogRotationTests : BlazorTestBase
{
    private static readonly Guid BranchId = Guid.NewGuid(), DepartmentId = Guid.NewGuid(), PositionId = Guid.NewGuid();
    private static readonly Guid MorningShift = Guid.NewGuid(), NightShift = Guid.NewGuid();

    public EmployeeDialogRotationTests()
    {
        SenderMock.Setup(s => s.Send(It.IsAny<GetBranchesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<BranchDto>>.Success(new[] { new BranchDto(BranchId, "S01", "Matriz", null, false, null) }));
        SenderMock.Setup(s => s.Send(It.IsAny<GetDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<DepartmentDto>>.Success(new[] { new DepartmentDto(DepartmentId, "Producción", null, new List<Guid> { PositionId }) }));
        SenderMock.Setup(s => s.Send(It.IsAny<GetPositionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<PositionDto>>.Success(new[] { new PositionDto { Id = PositionId, Name = "Operador" } }));
        SenderMock.Setup(s => s.Send(It.IsAny<GetShiftsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<ShiftDto>>.Success(new[]
            {
                Shift(MorningShift, "Mañana", 6),
                Shift(NightShift, "Noche", 22)
            }));
        SenderMock.Setup(s => s.Send(It.IsAny<GetRosterEmployeesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RosterEmployeeDto>
            {
                new("100", "Ana Pérez", "Producción", "Operador", "Matriz", BranchId, "Rotativo 3x8",
                    DateTime.Today.AddMonths(-1), DateTime.Today.AddMonths(11), 365, "Noche", HasRoster: true)
            });
    }

    [Fact]
    public async Task EditingARotatingEmployee_WithoutOpeningTheSchemeEditor_KeepsTheirRosterScheduleAndRestDays()
    {
        var result = await SaveRotatingEmployeeAsync(openEditor: false);

        result.RotationPatternCommand.Should().BeNull();
        result.UpdateCommand!.ScheduleId.Should().Be(NightShift.ToString());
        result.UpdateCommand.RestDays.Should().BeEquivalentTo(new[] { (int)WeekDay.Miercoles });
    }

    [Fact]
    public async Task ModifyingTheScheme_RegeneratesTheRoster()
    {
        var result = await SaveRotatingEmployeeAsync(openEditor: true);

        result.RotationPatternCommand.Should().NotBeNull();
        result.RotationPatternCommand!.EmployeeIds.Should().Equal("100");
    }

    private async Task<EmployeeDialogResult> SaveRotatingEmployeeAsync(bool openEditor)
    {
        var provider = RenderComponent<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<EmployeeDialog>
        {
            { x => x.EmployeeId, "100" },
            { x => x.FirstName, "Ana" },
            { x => x.LastName, "Pérez" },
            { x => x.HireDate, DateTime.Today.AddYears(-1) },
            { x => x.Gender, Gender.Female },
            { x => x.Status, EmployeeStatus.Alta },
            { x => x.BranchId, BranchId },
            { x => x.DepartmentId, DepartmentId },
            { x => x.PositionId, PositionId },
            { x => x.ShiftType, ShiftType.Rotativo },
            { x => x.ScheduleId, NightShift },
            { x => x.RestDays, new[] { (int)WeekDay.Miercoles } }
        };

        var dialog = await provider.InvokeAsync(() => dialogService.ShowAsync<EmployeeDialog>("Editar", parameters));
        // Los datos y el roster se cargan al abrir: se espera a que termine (desaparece la barra de carga).
        provider.WaitForAssertion(() => provider.FindAll(".mud-progress-linear").Should().BeEmpty());

        if (openEditor)
        {
            provider.FindAll(".mud-tab").First(t => t.TextContent.Trim() == "Horario").Click();
            provider.WaitForAssertion(() => provider.Markup.Should().Contain("Esquema Rotativo Activo"));
            provider.FindAll("button").First(b => b.TextContent.Contains("Modificar Esquema")).Click();
        }

        provider.FindAll("button").Last(b => b.TextContent.Trim() == "Guardar").Click();

        var result = await dialog.Result;
        result!.Canceled.Should().BeFalse();
        return (EmployeeDialogResult)result.Data!;
    }

    private static ShiftDto Shift(Guid id, string name, int startHour) =>
        new(id, name, TimeSpan.FromHours(startHour), TimeSpan.FromHours(startHour + 8), 10, TimeSpan.FromHours(8),
            ShiftType.Matutino, Enumerable.Empty<ShiftDayDto>(), false, 0);
}
