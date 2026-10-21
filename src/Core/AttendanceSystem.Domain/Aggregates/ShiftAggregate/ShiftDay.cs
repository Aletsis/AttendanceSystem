using System;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Aggregates.ShiftAggregate;

public record ShiftDay
{
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeSpan StartTime { get; private set; }
    public TimeSpan EndTime { get; private set; }
    public TimeSpan WorkHours { get; private set; }
    public ShiftType ShiftType { get; private set; }

    /// <summary>
    /// Horario existente que rige este día del turno mixto (decisión 5). Inicio, fin, duración y tipo se copian
    /// sólo para mostrar; el cálculo usa el horario referido completo. Null = formato anterior (horas por día).
    /// </summary>
    public ShiftId? DayShiftId { get; private set; }

    public bool IsLegacy => DayShiftId == null;

    private ShiftDay() { } // Para EF Core

    /// <summary>Día de un turno mixto regido por un horario existente (no mixto ni rotativo).</summary>
    public static ShiftDay ForShift(DayOfWeek dayOfWeek, Shift dayShift)
    {
        if (dayShift.ShiftType is ShiftType.Mixto or ShiftType.Rotativo)
            throw new DomainException($"El horario '{dayShift.Name}' es {dayShift.ShiftType}; un día de un turno mixto sólo admite horarios fijos, flexibles, continuos o partidos.");

        return new ShiftDay
        {
            DayOfWeek = dayOfWeek,
            DayShiftId = dayShift.Id,
            StartTime = dayShift.StartTime,
            EndTime = dayShift.EndTime,
            WorkHours = dayShift.WorkHours,
            ShiftType = dayShift.ShiftType
        };
    }

    public ShiftDay(DayOfWeek dayOfWeek, TimeSpan startTime, TimeSpan workHours, ShiftType shiftType = ShiftType.Matutino)
    {
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        WorkHours = workHours;
        ShiftType = shiftType;
        EndTime = NormalizeTime(startTime.Add(workHours));
    }

    private static TimeSpan NormalizeTime(TimeSpan time)
    {
        return time.TotalDays >= 1
            ? time.Subtract(TimeSpan.FromDays((int)time.TotalDays))
            : time;
    }
}
