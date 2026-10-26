using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
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

/// <summary>
/// Caso 13: permisos por campo del alta y la edición de empleados. El alta lleva horario (todo empleado lo necesita);
/// cambiarlo después exige <see cref="Permissions.EmployeesEditSchedule"/>. Las extras exigen
/// <see cref="Permissions.EmployeesEditOvertime"/> y el privilegio de administrador del reloj,
/// <see cref="Permissions.EmployeesDevicePrivilege"/>. Se comparan los valores normalizados: reenviar lo mismo no pide permiso.
/// </summary>
public static class EmployeeFieldPermissions
{
    public static string? ForCreate(IPermissionService? permissions, bool overtimeAuthorized, DevicePrivilege privilege)
    {
        if (overtimeAuthorized && permissions.Deny(Permissions.EmployeesEditOvertime) is { } overtime) return overtime;
        if (privilege != DevicePrivilege.User && permissions.Deny(Permissions.EmployeesDevicePrivilege) is { } device) return device;
        return null;
    }

    public static string? ForUpdate(IPermissionService? permissions, Employee employee, UpdateEmployeeCommand request)
    {
        if (permissions == null) return null;

        var requestedRestDays = (request.RestDays?.Select(d => (WeekDay)d)
            ?? (request.RestDay is { } day ? new[] { (WeekDay)day } : Array.Empty<WeekDay>())).ToHashSet();
        var scheduleChanged = request.Status != EmployeeStatus.Baja &&
            (employee.ShiftType != request.ShiftType ||
             (employee.ScheduleId?.Value.ToString() ?? "") != (string.IsNullOrWhiteSpace(request.ScheduleId) ? "" : ShiftId.From(request.ScheduleId).Value.ToString()) ||
             !employee.GetEffectiveRestDays().ToHashSet().SetEquals(requestedRestDays));
        if (scheduleChanged && permissions.Deny(Permissions.EmployeesEditSchedule) is { } schedule) return schedule;

        if (!SameOvertime(employee, request) && permissions.Deny(Permissions.EmployeesEditOvertime) is { } overtime) return overtime;

        if (employee.DevicePrivilege != request.DevicePrivilege && permissions.Deny(Permissions.EmployeesDevicePrivilege) is { } device) return device;

        // Caso 16: vacaciones, incapacidad y baja temporal son de RH y Administrador; alta y baja, de todos.
        var leaveChange = (Employee.IsLeaveStatus(request.Status) && (request.Status != employee.Status || request.EffectiveTo.HasValue)) ||
                          (Employee.IsLeaveStatus(employee.Status) && request.Status != employee.Status);
        if (leaveChange && permissions.Deny(Permissions.EmployeesLeave) is { } leave) return leave;
        return null;
    }

    private static bool SameOvertime(Employee employee, UpdateEmployeeCommand request)
    {
        if (employee.OvertimeAuthorized != request.OvertimeAuthorized) return false;
        if (!request.OvertimeAuthorized) return true; // sin extras no hay redondeo, topes ni extras antes de la entrada

        var beforeEntry = request.CalculateOvertimeBeforeEntry && Employee.AllowsOvertimeBeforeEntry(request.ShiftType);
        return employee.OvertimeCalculationMethod == request.OvertimeCalculationMethod &&
               employee.OvertimeCapType == request.OvertimeCapType &&
               (request.OvertimeCapType == OvertimeCapType.None || employee.OvertimeCapMinutes == request.OvertimeCapMinutes) &&
               employee.CalculateOvertimeBeforeEntry == beforeEntry;
    }
}
