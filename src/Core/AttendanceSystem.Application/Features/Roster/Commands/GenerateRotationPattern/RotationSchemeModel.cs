using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.Features.Roster.Commands.GenerateRotationPattern;

/// <summary>
/// Parámetros del esquema rotativo de un empleado, compartidos por la web y el cliente de escritorio:
/// la misma validación y el mismo <see cref="GenerateRotationPatternCommand"/> en ambos.
/// </summary>
public class RotationSchemeModel
{
    public RotationSchemeType SchemeType { get; set; } = RotationSchemeType.Rotativo3x8;
    public DateTime? StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; } = DateTime.Today.AddDays(30);
    public bool IsIndefinite { get; set; } = true;
    public int IndefiniteHorizonMonths { get; set; } = 12;

    public Guid Shift1 { get; set; }
    public Guid Shift2 { get; set; }
    public Guid Shift3 { get; set; }
    public Guid BaseShift { get; set; }

    public int DaysPerShift { get; set; } = 7;
    public int RestDaysAfterShift { get; set; } = 2;
    public RotationRestDayMode RestDayMode { get; set; } = RotationRestDayMode.FixedDaysOfWeek;
    public IReadOnlyCollection<DayOfWeek> SelectedFixedRestDays { get; set; } = new HashSet<DayOfWeek> { DayOfWeek.Sunday };

    public void InitializeShifts(IReadOnlyList<ShiftDto> shifts)
    {
        if (shifts == null || !shifts.Any()) return;

        if (BaseShift == Guid.Empty)
            BaseShift = shifts.First().Id;

        if (Shift1 == Guid.Empty)
            Shift1 = shifts.First().Id;

        if (Shift2 == Guid.Empty)
            Shift2 = shifts.Count > 1 ? shifts[1].Id : Shift1;

        if (Shift3 == Guid.Empty)
            Shift3 = shifts.Count > 2 ? shifts[2].Id : Shift1;
    }

    /// <summary>Turno que queda como horario del empleado: el primero de la rotación o el turno base.</summary>
    public Guid PrimaryShift =>
        SchemeType is RotationSchemeType.Rotativo3x8 or RotationSchemeType.Rotativo2x8 ? Shift1 : BaseShift;

    /// <summary>Los esquemas 3x8 y 2x8 con descanso en días fijos los guardan también como descansos del empleado.</summary>
    public IReadOnlyList<DayOfWeek>? ProfileRestDays =>
        SchemeType is RotationSchemeType.Rotativo3x8 or RotationSchemeType.Rotativo2x8 && SelectedFixedRestDays is { Count: > 0 }
            ? SelectedFixedRestDays.ToList()
            : null;

    public (bool IsValid, string? ErrorMessage) Validate()
    {
        if (!StartDate.HasValue)
            return (false, "Debe seleccionar la fecha de inicio del esquema rotativo.");

        if (!IsIndefinite && !EndDate.HasValue)
            return (false, "Debe seleccionar la fecha de fin o marcar el esquema como indefinido.");

        if ((SchemeType == RotationSchemeType.Rotativo3x8 || SchemeType == RotationSchemeType.Rotativo2x8)
            && RestDayMode == RotationRestDayMode.FixedDaysOfWeek
            && (SelectedFixedRestDays == null || !SelectedFixedRestDays.Any()))
        {
            return (false, "Debe seleccionar al menos un día fijo de descanso.");
        }

        if (SchemeType == RotationSchemeType.Rotativo3x8)
        {
            if (Shift1 == Guid.Empty || Shift2 == Guid.Empty || Shift3 == Guid.Empty)
                return (false, "Debe seleccionar los 3 turnos para el esquema 3x8.");
        }
        else if (SchemeType == RotationSchemeType.Rotativo2x8)
        {
            if (Shift1 == Guid.Empty || Shift2 == Guid.Empty)
                return (false, "Debe seleccionar los 2 turnos para el esquema 2x8.");
        }
        else
        {
            if (BaseShift == Guid.Empty)
                return (false, "Debe seleccionar el turno base para el esquema.");
        }

        return (true, null);
    }

    public GenerateRotationPatternCommand ToCommand(List<string> employeeIds)
    {
        var shiftIds = new List<Guid>();
        if (SchemeType == RotationSchemeType.Rotativo3x8)
        {
            shiftIds.AddRange(new[] { Shift1, Shift2, Shift3 });
        }
        else if (SchemeType == RotationSchemeType.Rotativo2x8)
        {
            shiftIds.AddRange(new[] { Shift1, Shift2 });
        }
        else
        {
            shiftIds.Add(BaseShift);
        }

        DateTime? targetEndDate = IsIndefinite
            ? StartDate!.Value.AddMonths(IndefiniteHorizonMonths)
            : EndDate;

        return new GenerateRotationPatternCommand(
            employeeIds,
            StartDate!.Value,
            targetEndDate,
            SchemeType,
            shiftIds,
            DaysPerShift,
            RestDaysAfterShift,
            IsIndefinite: IsIndefinite,
            RestDayMode: RestDayMode,
            FixedRestDays: SelectedFixedRestDays?.ToList());
    }
}
