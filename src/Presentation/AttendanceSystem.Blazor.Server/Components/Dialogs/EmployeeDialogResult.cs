using AttendanceSystem.Application.Features.Employees.Commands;
using AttendanceSystem.Application.Features.ExternalEmployees.Commands;
using AttendanceSystem.Application.Features.Roster.Commands.GenerateRotationPattern;

namespace AttendanceSystem.Blazor.Server.Components.Dialogs;

public class EmployeeDialogResult
{
    public CreateEmployeeCommand? CreateCommand { get; set; }
    public UpdateEmployeeCommand? UpdateCommand { get; set; }
    public CreateExternalEmployeeCommand? CreateExternalCommand { get; set; }
    public UpdateExternalEmployeeCommand? UpdateExternalCommand { get; set; }
    public GenerateRotationPatternCommand? RotationPatternCommand { get; set; }

    public bool IsExternal => CreateExternalCommand != null || UpdateExternalCommand != null;
    public bool IsCreate => CreateCommand != null || CreateExternalCommand != null;
}
