using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Shifts.Commands;

/// <summary>
/// Decisión 5: los días de un turno mixto se configuran eligiendo un horario existente por día (no mixto ni
/// rotativo); los días sin horario son descanso. Un turno no mixto no lleva días.
/// </summary>
internal static class MixedShiftDays
{
    public static async Task<Result<List<ShiftDay>?>> BuildAsync(
        ShiftType shiftType,
        IEnumerable<ShiftDayDto>? days,
        IShiftRepository shiftRepository,
        ShiftId? selfId,
        CancellationToken cancellationToken)
    {
        if (shiftType != ShiftType.Mixto) return Result<List<ShiftDay>?>.Success(null);

        var result = new List<ShiftDay>();
        foreach (var day in days ?? Enumerable.Empty<ShiftDayDto>())
        {
            if (day.DayShiftId is not { } dayShiftGuid)
            {
                return Result<List<ShiftDay>?>.Failure($"Seleccione el horario del {DayName(day.DayOfWeek)} o márquelo como descanso.");
            }

            var dayShiftId = ShiftId.From(dayShiftGuid);
            if (selfId != null && dayShiftId == selfId)
            {
                return Result<List<ShiftDay>?>.Failure("Un turno mixto no puede usarse a sí mismo como horario de un día.");
            }

            var dayShift = await shiftRepository.GetByIdAsync(dayShiftId, cancellationToken);
            if (dayShift is null)
            {
                return Result<List<ShiftDay>?>.Failure($"No existe el horario seleccionado para el {DayName(day.DayOfWeek)}.");
            }
            if (dayShift.ShiftType is ShiftType.Mixto or ShiftType.Rotativo)
            {
                return Result<List<ShiftDay>?>.Failure($"El horario '{dayShift.Name}' es {dayShift.ShiftType}; no puede asignarse a un día de un turno mixto.");
            }

            result.Add(ShiftDay.ForShift(day.DayOfWeek, dayShift));
        }

        if (result.Count == 0)
        {
            return Result<List<ShiftDay>?>.Failure("Un turno mixto debe tener al menos un día con horario.");
        }
        return Result<List<ShiftDay>?>.Success(result);
    }

    public static string DayName(DayOfWeek day) => day switch
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
