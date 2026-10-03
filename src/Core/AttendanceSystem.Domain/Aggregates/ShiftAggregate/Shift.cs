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
        TimeSpan? weeklyWorkHours = null)
    {
        if (shiftType == ShiftType.Flexible)
        {
            flexWindowEndTime ??= startTime;
            if (flexWindowEndTime.Value < startTime)
                throw new DomainException("El fin de la ventana de llegada no puede ser anterior a la hora de inicio.");
        }
        else
        {
            flexWindowEndTime = null;
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
            EndTime = NormalizeTime(startTime.Add(workHours)),
            RoundingsEnabled = roundingsEnabled,
            RoundingInterval = roundingInterval,
            FlexWindowEndTime = flexWindowEndTime,
            WeeklyWorkHours = weeklyWorkHours
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
        TimeSpan? weeklyWorkHours = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del turno es requerido.");
        if (toleranceMinutes < 0)
            throw new DomainException("El tiempo de tolerancia no puede ser negativo.");

        if (shiftType == ShiftType.Flexible)
        {
            flexWindowEndTime ??= startTime;
            if (flexWindowEndTime.Value < startTime)
                throw new DomainException("El fin de la ventana de llegada no puede ser anterior a la hora de inicio.");
        }
        else
        {
            flexWindowEndTime = null;
        }

        Name = name;
        StartTime = startTime;
        ToleranceMinutes = toleranceMinutes;
        WorkHours = workHours;
        ShiftType = shiftType;
        LunchBreakMinutes = lunchBreakMinutes < 0 ? 0 : lunchBreakMinutes;
        EndTime = NormalizeTime(startTime.Add(workHours));
        RoundingsEnabled = roundingsEnabled;
        RoundingInterval = roundingInterval;
        FlexWindowEndTime = flexWindowEndTime;
        WeeklyWorkHours = weeklyWorkHours;

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
