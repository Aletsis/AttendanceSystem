using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Application.Features.Positions.Queries.GetPositions;
using AttendanceSystem.Blazor.Server.Components.Dialogs;
using AttendanceSystem.Blazor.UnitTests.Common;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace AttendanceSystem.Blazor.UnitTests.Dialogs;

public class DialogComponentTests : BlazorTestBase
{
    [Fact]
    public async Task ConfirmationDialog_ShouldRenderContentAndButtons()
    {
        var comp = RenderComponent<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ConfirmationDialog>
        {
            { x => x.ContentText, "¿Está seguro de eliminar este registro?" },
            { x => x.ButtonText, "Confirmar" },
            { x => x.Color, Color.Error }
        };

        await comp.InvokeAsync(() => dialogService.ShowAsync<ConfirmationDialog>("Título", parameters));

        comp.Markup.Should().Contain("¿Está seguro de eliminar este registro?");
        comp.Markup.Should().Contain("Confirmar");
        comp.Markup.Should().Contain("Cancelar");
    }

    [Fact]
    public async Task ConfirmDialog_ShouldRenderContentAndButtons()
    {
        var comp = RenderComponent<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.ContentText, "¿Desea guardar los cambios?" },
            { x => x.ButtonText, "Aceptar" }
        };

        await comp.InvokeAsync(() => dialogService.ShowAsync<ConfirmDialog>("Título", parameters));

        comp.Markup.Should().Contain("¿Desea guardar los cambios?");
        comp.Markup.Should().Contain("Aceptar");
    }

    [Fact]
    public async Task BranchDialog_ShouldRenderFields()
    {
        var comp = RenderComponent<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<BranchDialog>
        {
            { x => x.Code, "A01" },
            { x => x.Name, "Sucursal Norte" }
        };

        await comp.InvokeAsync(() => dialogService.ShowAsync<BranchDialog>("Título", parameters));

        comp.Markup.Should().Contain("Código");
        comp.Markup.Should().Contain("Nombre");
        comp.Markup.Should().Contain("Dirección");
        comp.Markup.Should().Contain("Guardar");
    }

    [Fact]
    public async Task DepartmentDialog_ShouldRenderAndLoadPositions()
    {
        SenderMock.Setup(s => s.Send(It.IsAny<GetPositionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IEnumerable<PositionDto>>.Success(new List<PositionDto>
            {
                new() { Id = Guid.NewGuid(), Name = "Ingeniero de Software" }
            }));

        var comp = RenderComponent<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<DepartmentDialog>
        {
            { x => x.Name, "Sistemas" }
        };

        await comp.InvokeAsync(() => dialogService.ShowAsync<DepartmentDialog>("Título", parameters));

        comp.Markup.Should().Contain("Nombre");
        comp.Markup.Should().Contain("Descripción");
        comp.Markup.Should().Contain("Puestos");
    }

    [Fact]
    public async Task PositionDialog_ShouldRenderFields()
    {
        var comp = RenderComponent<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<PositionDialog>
        {
            { x => x.Name, "Auditor" },
            { x => x.Description, "Auditoría interna" }
        };

        await comp.InvokeAsync(() => dialogService.ShowAsync<PositionDialog>("Título", parameters));

        comp.Markup.Should().Contain("Nombre");
        comp.Markup.Should().Contain("Descripción");
        comp.Markup.Should().Contain("Guardar");
    }

    [Fact]
    public async Task FolderPickerDialog_ShouldRenderDirectoryBrowser()
    {
        var comp = RenderComponent<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<FolderPickerDialog>
        {
            { x => x.InitialPath, "/tmp" }
        };

        await comp.InvokeAsync(() => dialogService.ShowAsync<FolderPickerDialog>("Título", parameters));

        comp.Markup.Should().Contain("Seleccionar Carpeta");
        comp.Markup.Should().Contain("Seleccionar");
    }

    [Fact]
    public async Task ShiftDialog_WithSplitShift_ShouldRenderAndSubmitCorrectBlock1Duration()
    {
        var comp = RenderComponent<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();
        var shiftId = Guid.NewGuid();
        var parameters = new DialogParameters<ShiftDialog>
        {
            { x => x.ShiftId, shiftId },
            { x => x.Name, "Turno Partido Restaurante" },
            { x => x.ShiftType, AttendanceSystem.Domain.Enumerations.ShiftType.Partido },
            { x => x.StartTime, new TimeSpan(9, 0, 0) },
            { x => x.EndTime, new TimeSpan(13, 0, 0) },
            { x => x.WorkHours, new TimeSpan(8, 0, 0) },
            { x => x.ToleranceMinutes, 10 },
            { x => x.SecondBlockStartTime, new TimeSpan(17, 0, 0) },
            { x => x.SecondBlockEndTime, new TimeSpan(21, 0, 0) },
            { x => x.SecondBlockToleranceMinutes, 15 }
        };

        var dialogRef = await comp.InvokeAsync(() => dialogService.ShowAsync<ShiftDialog>("Editar Turno", parameters));

        comp.Markup.Should().Contain("Primer Bloque");
        comp.Markup.Should().Contain("Segundo Bloque");
        comp.Markup.Should().Contain("Entrada Bloque 1");
        comp.Markup.Should().Contain("Salida Bloque 1");
        comp.Markup.Should().Contain("Entrada Bloque 2");
        comp.Markup.Should().Contain("Salida Bloque 2");

        // Submit the dialog
        var buttons = comp.FindAll("button");
        var saveButton = buttons.First(b => b.TextContent.Contains("Guardar"));
        await comp.InvokeAsync(() => saveButton.Click());

        var result = await dialogRef.Result;
        result.Should().NotBeNull();
        result!.Canceled.Should().BeFalse();
        result.Data.Should().BeOfType<AttendanceSystem.Application.Features.Shifts.Commands.UpdateShift.UpdateShiftCommand>();

        var cmd = (AttendanceSystem.Application.Features.Shifts.Commands.UpdateShift.UpdateShiftCommand)result.Data!;
        cmd.StartTime.Should().Be(new TimeSpan(9, 0, 0));
        // Crucial fix: workHours sent to command must be the duration of block 1 (13:00 - 09:00 = 4h)
        cmd.WorkHours.Should().Be(new TimeSpan(4, 0, 0));
        cmd.SecondBlockStartTime.Should().Be(new TimeSpan(17, 0, 0));
        cmd.SecondBlockEndTime.Should().Be(new TimeSpan(21, 0, 0));
    }
}
