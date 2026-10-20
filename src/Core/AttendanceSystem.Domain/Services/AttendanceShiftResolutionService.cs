namespace AttendanceSystem.Domain.Services;

using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftRosterAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;

public sealed record ShiftResolutionResult(
    Shift? Shift,
    bool IsRestDay,
    bool IsAutoDetected,
    bool IsRestDayOverridden,
    bool IsShiftOverridden);

/// <summary>
/// Servicio de dominio que centraliza la resolución de turno y descanso diario,
/// garantizando consistencia estricta entre el cálculo automático y las modificaciones manuales.
/// Precedencia:
/// 1. Ajuste manual explícito del día (AttendanceAdjustment)
/// 2. Calendario / Roster planificado (ShiftRoster)
/// 3. Reglas base del empleado (RestDays con fallback a RestDay, horario fijo o autodetección)
/// </summary>
public static class AttendanceShiftResolutionService
{
    public static ShiftResolutionResult Resolve(
        Employee employee,
        DateTime date,
        ShiftRoster? roster,
        AttendanceAdjustment? adjustment,
        IReadOnlyDictionary<ShiftId, Shift> availableShifts,
        IEnumerable<DateTime>? dayPunches = null)
    {
        bool isRestDay = false;
        bool isRestDayOverridden = false;
        bool isShiftOverridden = false;
        bool isAutoDetected = false;
        Shift? shift = null;

        // 1. Precedencia 1: Ajuste manual explícito del día (RH)
        if (adjustment != null)
        {
            if (adjustment.IsRestDayOverride.HasValue)
            {
                isRestDay = adjustment.IsRestDayOverride.Value;
                isRestDayOverridden = true;
            }

            if (adjustment.ShiftIdOverride != null &&
                availableShifts.TryGetValue(adjustment.ShiftIdOverride, out var adjShift))
            {
                shift = adjShift;
                isShiftOverridden = true;
            }
        }

        // Si ya hay override explícito de descanso y turno, no requiere consultar niveles inferiores
        if (isRestDayOverridden && shift != null)
        {
            return new ShiftResolutionResult(shift, isRestDay, isAutoDetected, isRestDayOverridden, isShiftOverridden);
        }

        // 2. Precedencia 2: Asignación en Roster / Calendario de turnos
        if (roster != null)
        {
            if (!isRestDayOverridden)
            {
                isRestDay = roster.IsRestDay;
            }

            if (shift == null && roster.ShiftId != null &&
                availableShifts.TryGetValue(roster.ShiftId, out var rShift))
            {
                shift = rShift;
            }
        }
        else if (!isRestDayOverridden)
        {
            // Determinar día(s) de descanso predeterminados del empleado (evaluando RestDays múltiples con fallback a RestDay)
            if (employee.IsRestDay(date.DayOfWeek))
            {
                isRestDay = true;
            }
        }

        // 3. Precedencia 3: Detección automática para turno rotativo o sin horario fijo, con la asignación
        //    vigente en la fecha calculada (F06-08), no la configuración actual del empleado.
        var assignment = employee.AssignmentOn(date);
        if (shift == null)
        {
            if (assignment.ShiftType == ShiftType.Rotativo || assignment.ScheduleId == null)
            {
                // F06-03 / H10: sólo hay candidato si existe un marcaje real (los del día ya excluyen los que
                // pertenecen a jornadas previas). FirstOrDefault sobre DateTime devolvía MinValue y autodetectaba.
                DateTime? firstPunch = dayPunches?.OrderBy(p => p).Select(p => (DateTime?)p).FirstOrDefault();
                if (firstPunch.HasValue)
                {
                    var autoShift = ShiftDetectionService.FindClosestShift(firstPunch.Value, availableShifts.Values);
                    if (autoShift != null)
                    {
                        shift = autoShift;
                        isAutoDetected = true;
                    }
                }
            }

            // 4. Precedencia 4: Turno fijo del empleado (ScheduleId)
            if (shift == null && assignment.ScheduleId != null &&
                availableShifts.TryGetValue(assignment.ScheduleId, out var empShift))
            {
                shift = empShift;
            }
        }

        return new ShiftResolutionResult(shift, isRestDay, isAutoDetected, isRestDayOverridden, isShiftOverridden);
    }
}
