using System.Text.Json;
using AttendanceSystem.Application.DTOs;

namespace AttendanceSystem.Application.Features.Roster.Commands.GenerateRotationPattern;

/// <summary>Lo que se guarda de un esquema rotativo para poder seguir generando su roster.</summary>
public sealed record RotationPatternDefinition(
    RotationSchemeType SchemeType,
    List<Guid> ShiftIds,
    int DaysPerShift,
    int RestDaysAfterRotation,
    List<CustomRotationSlotDto>? CustomSlots,
    RotationRestDayMode RestDayMode,
    List<DayOfWeek>? FixedRestDays)
{
    public static RotationPatternDefinition From(GenerateRotationPatternCommand command) => new(
        command.SchemeType, command.ShiftIds ?? new List<Guid>(), command.DaysPerShift, command.RestDaysAfterRotation,
        command.CustomSlots, command.RestDayMode, command.FixedRestDays);

    /// <summary>El comando equivalente para un empleado, con el inicio original para que el ciclo continúe.</summary>
    public GenerateRotationPatternCommand ToCommand(string employeeId, DateTime startDate) => new(
        new List<string> { employeeId }, startDate, null, SchemeType, ShiftIds, DaysPerShift, RestDaysAfterRotation,
        CustomSlots, IsIndefinite: true, RestDayMode, FixedRestDays);

    public string Serialize() => JsonSerializer.Serialize(this);

    public static RotationPatternDefinition Deserialize(string json) =>
        JsonSerializer.Deserialize<RotationPatternDefinition>(json) ?? throw new InvalidOperationException("Patrón rotativo inválido.");

    /// <summary>Un esquema necesita al menos un turno; si no, todos sus días serían descanso.</summary>
    public string? MissingShiftsError()
    {
        if (SchemeType == RotationSchemeType.Personalizado)
        {
            return CustomSlots == null || !CustomSlots.Any(s => !s.IsRestDay && s.ShiftId is { } id && id != Guid.Empty)
                ? "El patrón personalizado debe tener al menos un día con turno."
                : null;
        }

        return ShiftIds.Count == 0 || ShiftIds.Any(id => id == Guid.Empty)
            ? "Seleccione los turnos del esquema rotativo."
            : null;
    }
}
