using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.Features.Employees.Commands;

/// <summary>
/// Caso 10: ningún empleado queda sin turno. Todo empleado tiene tipo de turno y, salvo el rotativo (que sigue su
/// roster), un horario.
/// </summary>
internal static class EmployeeScheduleRules
{
    public static string? Validate(ShiftType? shiftType, string? scheduleId)
    {
        if (!shiftType.HasValue) return "Seleccione el turno del empleado.";
        if (shiftType.Value != ShiftType.Rotativo && string.IsNullOrWhiteSpace(scheduleId)) return "Seleccione el horario del empleado.";
        return null;
    }
}
