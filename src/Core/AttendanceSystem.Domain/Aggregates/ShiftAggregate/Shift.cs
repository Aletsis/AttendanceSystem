using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Aggregates.ShiftAggregate;

public class Shift : AggregateRoot<ShiftId>
{
    public string Name { get; private set; } = null!;
    public TimeSpan StartTime { get; private set; }
    public TimeSpan EndTime { get; private set; }
    public int ToleranceMinutes { get; private set; }
    public TimeSpan WorkHours { get; private set; }
    public ShiftType ShiftType { get; private set; }
    /// <summary>Minutos de descanso de comida a deducir del tiempo laborado (0 = sin deducción automática).</summary>
    public int LunchBreakMinutes { get; private set; }
    public bool RoundingsEnabled { get; private set; }
    public int RoundingInterval { get; private set; }
    /// <summary>Hora límite de la ventana de llegada para turnos flexibles (null si no es flexible).</summary>
    public TimeSpan? FlexWindowEndTime { get; private set; }
    /// <summary>Horas objetivo semanales para cálculo de bolsa de horas acumulada (opcional).</summary>
    public TimeSpan? WeeklyWorkHours { get; private set; }
    /// <summary>Hora de inicio del segundo bloque para turnos partidos.</summary>
    public TimeSpan? SecondBlockStartTime { get; private set; }
    /// <summary>Hora de fin del segundo bloque para turnos partidos.</summary>
    public TimeSpan? SecondBlockEndTime { get; private set; }
    /// <summary>Minutos de tolerancia para el segundo bloque.</summary>
    public int? SecondBlockToleranceMinutes { get; private set; }
    /// <summary>Modalidad de seguimiento de marcajes (tiempo corrido vs multi-marcaje acumulable).</summary>
    public PunchTrackingMode PunchTrackingMode { get; private set; } = PunchTrackingMode.SingleInterval;
    /// <summary>Indica si el turno tiene ventana de llegada (false = entrada abierta a cualquier hora sin retardo).</summary>
    public bool HasEntryWindow { get; private set; } = true;

    private readonly List<ShiftDay> _days = new();
    public IReadOnlyCollection<ShiftDay> Days => _days.AsReadOnly();

    private Shift() { }

    public static Shift Create(
        string name,
        TimeSpan startTime,
        int toleranceMinutes,
        TimeSpan workHours,
        ShiftType shiftType,
        IEnumerable<ShiftDay>? days = null,
        int lunchBreakMinutes = 0,
        bool roundingsEnabled = false,
        int roundingInterval = 0,
        TimeSpan? flexWindowEndTime = null,
        TimeSpan? weeklyWorkHours = null,
        TimeSpan? secondBlockStartTime = null,
        TimeSpan? secondBlockEndTime = null,
        int? secondBlockToleranceMinutes = null,
        PunchTrackingMode punchTrackingMode = PunchTrackingMode.SingleInterval,
        bool hasEntryWindow = true)
    {
        TimeSpan calculatedEndTime;

        if (shiftType == ShiftType.Flexible)
        {
            if (hasEntryWindow)
            {
                flexWindowEndTime ??= startTime;
                if (flexWindowEndTime.Value < startTime)
                    throw new DomainException("El fin de la ventana de llegada no puede ser anterior a la hora de inicio.");
            }
            else
            {
                startTime = TimeSpan.Zero;
                flexWindowEndTime = null;
            }

            bool hasDaily = workHours > TimeSpan.Zero;
            bool hasWeekly = weeklyWorkHours.HasValue && weeklyWorkHours.Value > TimeSpan.Zero;

            if (hasDaily && hasWeekly)
                throw new DomainException("En un turno flexible, las horas objetivo deben configurarse por día o por semana, pero no ambas.");

            if (!hasDaily && !hasWeekly)
                throw new DomainException("En un turno flexible, debe especificarse las horas objetivo diarias o las horas objetivo semanales.");

            if (!hasDaily)
            {
                workHours = TimeSpan.Zero;
            }
            else
            {
                weeklyWorkHours = null;
            }

            calculatedEndTime = NormalizeTime(startTime.Add(workHours));
            secondBlockStartTime = null;
            secondBlockEndTime = null;
            secondBlockToleranceMinutes = null;
        }
        else if (shiftType == ShiftType.Continuo)
        {
            hasEntryWindow = false;
            flexWindowEndTime = null;
            weeklyWorkHours = null;
            secondBlockStartTime = null;
            secondBlockEndTime = null;
            secondBlockToleranceMinutes = null;

            if (workHours <= TimeSpan.Zero)
                throw new DomainException("Las horas de trabajo diarias deben ser mayores a cero.");

            calculatedEndTime = NormalizeTime(startTime.Add(workHours));
        }
        else if (shiftType == ShiftType.Partido)
        {
            if (!secondBlockStartTime.HasValue)
                throw new DomainException("La hora de inicio del segundo bloque es requerida para turnos partidos.");
            if (!secondBlockEndTime.HasValue)
                throw new DomainException("La hora de fin del segundo bloque es requerida para turnos partidos.");

            var b1End = NormalizeTime(startTime.Add(workHours));
            if (startTime == b1End)
                throw new DomainException("El primer bloque debe tener una duración mayor a cero.");

            if (secondBlockStartTime.Value == secondBlockEndTime.Value)
                throw new DomainException("El segundo bloque debe tener una duración mayor a cero.");

            if (secondBlockStartTime.Value < b1End)
                throw new DomainException("El segundo bloque no puede iniciar antes de que finalice el primer bloque.");

            secondBlockToleranceMinutes ??= toleranceMinutes;
            if (secondBlockToleranceMinutes.Value < 0)
                throw new DomainException("El tiempo de tolerancia del segundo bloque no puede ser negativo.");

            var b1Duration = b1End >= startTime ? b1End - startTime : b1End.Add(TimeSpan.FromDays(1)) - startTime;
            var b2Duration = secondBlockEndTime.Value >= secondBlockStartTime.Value
                ? secondBlockEndTime.Value - secondBlockStartTime.Value
                : secondBlockEndTime.Value.Add(TimeSpan.FromDays(1)) - secondBlockStartTime.Value;

            workHours = b1Duration + b2Duration;
            calculatedEndTime = b1End;
            flexWindowEndTime = null;
            weeklyWorkHours = null;
        }
        else
        {
            flexWindowEndTime = null;
            weeklyWorkHours = null;
            secondBlockStartTime = null;
            secondBlockEndTime = null;
            secondBlockToleranceMinutes = null;

            if (workHours <= TimeSpan.Zero)
                throw new DomainException("Las horas de trabajo diarias deben ser mayores a cero.");

            calculatedEndTime = NormalizeTime(startTime.Add(workHours));
        }

        var shift = new Shift
        {
            Id = ShiftId.CreateNew(),
            Name = name,
            StartTime = startTime,
            ToleranceMinutes = toleranceMinutes,
            WorkHours = workHours,
            ShiftType = shiftType,
            LunchBreakMinutes = lunchBreakMinutes < 0 ? 0 : lunchBreakMinutes,
            EndTime = calculatedEndTime,
            RoundingsEnabled = roundingsEnabled,
            RoundingInterval = roundingInterval,
            FlexWindowEndTime = flexWindowEndTime,
            WeeklyWorkHours = weeklyWorkHours,
            SecondBlockStartTime = secondBlockStartTime,
            SecondBlockEndTime = secondBlockEndTime,
            SecondBlockToleranceMinutes = secondBlockToleranceMinutes,
            PunchTrackingMode = punchTrackingMode,
            HasEntryWindow = (shiftType == ShiftType.Continuo) ? false : hasEntryWindow
        };

        if (days != null && days.Any())
        {
            shift._days.AddRange(days);
        }

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del turno es requerido.");
        if (toleranceMinutes < 0)
            throw new DomainException("El tiempo de tolerancia no puede ser negativo.");

        return shift;
    }

    public void Update(
        string name,
        TimeSpan startTime,
        int toleranceMinutes,
        TimeSpan workHours,
        ShiftType shiftType,
        IEnumerable<ShiftDay>? days = null,
        int lunchBreakMinutes = 0,
        bool roundingsEnabled = false,
        int roundingInterval = 0,
        TimeSpan? flexWindowEndTime = null,
        TimeSpan? weeklyWorkHours = null,
        TimeSpan? secondBlockStartTime = null,
        TimeSpan? secondBlockEndTime = null,
        int? secondBlockToleranceMinutes = null,
        PunchTrackingMode punchTrackingMode = PunchTrackingMode.SingleInterval,
        bool hasEntryWindow = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del turno es requerido.");
        if (toleranceMinutes < 0)
            throw new DomainException("El tiempo de tolerancia no puede ser negativo.");

        TimeSpan calculatedEndTime;

        if (shiftType == ShiftType.Flexible)
        {
            if (hasEntryWindow)
            {
                flexWindowEndTime ??= startTime;
                if (flexWindowEndTime.Value < startTime)
                    throw new DomainException("El fin de la ventana de llegada no puede ser anterior a la hora de inicio.");
            }
            else
            {
                startTime = TimeSpan.Zero;
                flexWindowEndTime = null;
            }

            bool hasDaily = workHours > TimeSpan.Zero;
            bool hasWeekly = weeklyWorkHours.HasValue && weeklyWorkHours.Value > TimeSpan.Zero;

            if (hasDaily && hasWeekly)
                throw new DomainException("En un turno flexible, las horas objetivo deben configurarse por día o por semana, pero no ambas.");

            if (!hasDaily && !hasWeekly)
                throw new DomainException("En un turno flexible, debe especificarse las horas objetivo diarias o las horas objetivo semanales.");

            if (!hasDaily)
            {
                workHours = TimeSpan.Zero;
            }
            else
            {
                weeklyWorkHours = null;
            }

            calculatedEndTime = NormalizeTime(startTime.Add(workHours));
            secondBlockStartTime = null;
            secondBlockEndTime = null;
            secondBlockToleranceMinutes = null;
        }
        else if (shiftType == ShiftType.Continuo)
        {
            hasEntryWindow = false;
            flexWindowEndTime = null;
            weeklyWorkHours = null;
            secondBlockStartTime = null;
            secondBlockEndTime = null;
            secondBlockToleranceMinutes = null;

            if (workHours <= TimeSpan.Zero)
                throw new DomainException("Las horas de trabajo diarias deben ser mayores a cero.");

            calculatedEndTime = NormalizeTime(startTime.Add(workHours));
        }
        else if (shiftType == ShiftType.Partido)
        {
            if (!secondBlockStartTime.HasValue)
                throw new DomainException("La hora de inicio del segundo bloque es requerida para turnos partidos.");
            if (!secondBlockEndTime.HasValue)
                throw new DomainException("La hora de fin del segundo bloque es requerida para turnos partidos.");

            var b1End = NormalizeTime(startTime.Add(workHours));
            if (startTime == b1End)
                throw new DomainException("El primer bloque debe tener una duración mayor a cero.");

            if (secondBlockStartTime.Value == secondBlockEndTime.Value)
                throw new DomainException("El segundo bloque debe tener una duración mayor a cero.");

            if (secondBlockStartTime.Value < b1End)
                throw new DomainException("El segundo bloque no puede iniciar antes de que finalice el primer bloque.");

            secondBlockToleranceMinutes ??= toleranceMinutes;
            if (secondBlockToleranceMinutes.Value < 0)
                throw new DomainException("El tiempo de tolerancia del segundo bloque no puede ser negativo.");

            var b1Duration = b1End >= startTime ? b1End - startTime : b1End.Add(TimeSpan.FromDays(1)) - startTime;
            var b2Duration = secondBlockEndTime.Value >= secondBlockStartTime.Value
                ? secondBlockEndTime.Value - secondBlockStartTime.Value
                : secondBlockEndTime.Value.Add(TimeSpan.FromDays(1)) - secondBlockStartTime.Value;

            workHours = b1Duration + b2Duration;
            calculatedEndTime = b1End;
            flexWindowEndTime = null;
            weeklyWorkHours = null;
        }
        else
        {
            flexWindowEndTime = null;
            weeklyWorkHours = null;
            secondBlockStartTime = null;
            secondBlockEndTime = null;
            secondBlockToleranceMinutes = null;

            if (workHours <= TimeSpan.Zero)
                throw new DomainException("Las horas de trabajo diarias deben ser mayores a cero.");

            calculatedEndTime = NormalizeTime(startTime.Add(workHours));
        }

        Name = name;
        StartTime = startTime;
        ToleranceMinutes = toleranceMinutes;
        WorkHours = workHours;
        ShiftType = shiftType;
        LunchBreakMinutes = lunchBreakMinutes < 0 ? 0 : lunchBreakMinutes;
        EndTime = calculatedEndTime;
        RoundingsEnabled = roundingsEnabled;
        RoundingInterval = roundingInterval;
        FlexWindowEndTime = flexWindowEndTime;
        WeeklyWorkHours = weeklyWorkHours;
        SecondBlockStartTime = secondBlockStartTime;
        SecondBlockEndTime = secondBlockEndTime;
        SecondBlockToleranceMinutes = secondBlockToleranceMinutes;
        PunchTrackingMode = punchTrackingMode;
        HasEntryWindow = (shiftType == ShiftType.Continuo) ? false : hasEntryWindow;

        _days.Clear();
        if (days != null && days.Any())
        {
            _days.AddRange(days);
        }
    }

    private static TimeSpan NormalizeTime(TimeSpan time)
    {
        return time.TotalDays >= 1
            ? time.Subtract(TimeSpan.FromDays((int)time.TotalDays))
            : time;
    }
}
