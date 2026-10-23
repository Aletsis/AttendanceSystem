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
    bool IsShiftOverridden,
    string? Issue = null);

/// <summary>
/// Servicio de dominio que centraliza la resolución de turno y descanso diario,
/// garantizando consistencia estricta entre el cálculo automático y las modificaciones manuales.
/// Precedencia (con la asignación vigente en la fecha, F06-08):
/// 1. Ajuste manual explícito del día (AttendanceAdjustment): turno y/o descanso.
/// 2. Horario fijo del empleado; el roster no aplica. Descanso: los días de descanso vigentes del empleado.
/// 3. Empleado rotativo: el roster del día (turno y descanso); sin roster ese día, autodetección y sus días de descanso.
/// 4. Sin horario: autodetección entre turnos matutinos, vespertinos y nocturnos; sus días de descanso.
/// </summary>
public static class AttendanceShiftResolutionService
{
    /// <summary>Tipos de turno candidatos de la autodetección.</summary>
    public static readonly IReadOnlySet<ShiftType> AutoDetectableTypes =
        new HashSet<ShiftType> { ShiftType.Matutino, ShiftType.Vespertino, ShiftType.Nocturno };

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

        // 1. Ajuste manual explícito del día (RH)
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

        var assignment = employee.AssignmentOn(date);
        bool isRotating = assignment.ShiftType == ShiftType.Rotativo;
        // El roster sólo rige a quien es rotativo en esa fecha (un horario fijo posterior lo deja sin efecto).
        var rosterEntry = isRotating ? roster : null;

        if (!isRestDayOverridden)
        {
            isRestDay = rosterEntry?.IsRestDay ?? employee.IsRestDayOn(date);
        }

        if (shift == null)
        {
            if (isRotating)
            {
                // 3. Rotativo: turno del roster; sin roster ese día, autodetección; en último caso su horario.
                if (rosterEntry?.ShiftId != null && availableShifts.TryGetValue(rosterEntry.ShiftId, out var rShift))
                {
                    shift = rShift;
                }
                else
                {
                    shift = AutoDetect(dayPunches, availableShifts);
                    isAutoDetected = shift != null;
                }
            }
            else if (assignment.ScheduleId == null)
            {
                // 4. Sin horario: autodetección.
                shift = AutoDetect(dayPunches, availableShifts);
                isAutoDetected = shift != null;
            }

            // 2. Horario fijo (y respaldo del rotativo sin roster ni turno cercano).
            if (shift == null && assignment.ScheduleId != null &&
                availableShifts.TryGetValue(assignment.ScheduleId, out var empShift))
            {
                shift = empShift;
            }
        }

        // 5. Decisión 5: un turno mixto se sustituye por el horario de ese día, que rige el cálculo completo.
        string? issue = null;
        if (shift?.ShiftType == ShiftType.Mixto)
        {
            var mixed = shift;
            var day = mixed.Days.FirstOrDefault(d => d.DayOfWeek == date.DayOfWeek);
            if (day == null)
            {
                // Día sin horario en la plantilla: descanso salvo decisión manual explícita (C-05).
                shift = null;
                if (!isRestDayOverridden) isRestDay = true;
            }
            else if (day.DayShiftId is { } dayShiftId)
            {
                if (availableShifts.TryGetValue(dayShiftId, out var dayShift) && dayShift.ShiftType is not (ShiftType.Mixto or ShiftType.Rotativo))
                {
                    shift = dayShift;
                }
                else
                {
                    shift = null;
                    issue = $"El horario asignado al {DayName(date.DayOfWeek)} en el turno mixto '{mixed.Name}' no existe o no es válido.";
                }
            }
            else
            {
                // Formato anterior (horas por día): se calcula como antes hasta que RH asigne un horario al día.
                shift = mixed.EffectiveFor(day);
                issue = $"Turno mixto '{mixed.Name}' con horas por día (formato anterior): asigne un horario al {DayName(date.DayOfWeek)}.";
            }
        }

        return new ShiftResolutionResult(shift, isRestDay, isAutoDetected, isRestDayOverridden, isShiftOverridden, issue);
    }

    /// <summary>
    /// F06-03 / H10: sólo hay candidato si existe un marcaje real (los del día ya excluyen los que pertenecen a
    /// jornadas previas). Se busca el turno matutino, vespertino o nocturno con el inicio más cercano al primero.
    /// </summary>
    private static Shift? AutoDetect(IEnumerable<DateTime>? dayPunches, IReadOnlyDictionary<ShiftId, Shift> availableShifts)
    {
        DateTime? firstPunch = dayPunches?.OrderBy(p => p).Select(p => (DateTime?)p).FirstOrDefault();
        if (!firstPunch.HasValue) return null;
        return ShiftDetectionService.FindClosestShift(firstPunch.Value,
            availableShifts.Values.Where(s => AutoDetectableTypes.Contains(s.ShiftType)));
    }

    private static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "lunes",
        DayOfWeek.Tuesday => "martes",
        DayOfWeek.Wednesday => "miércoles",
        DayOfWeek.Thursday => "jueves",
        DayOfWeek.Friday => "viernes",
        DayOfWeek.Saturday => "sábado",
        _ => "domingo"
    };
}
