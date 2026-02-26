using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Aggregates.ShiftAggregate;

public class Shift : AggregateRoot<ShiftId>
{
    public string Name { get; private set; } = null!;
    public TimeSpan StartTime { get; private set; }
    public TimeSpan EndTime { get; private set; } // Calculated or explicit? Usually Start + WorkHours.
    public int ToleranceMinutes { get; private set; }
    public TimeSpan WorkHours { get; private set; }
    public ShiftType ShiftType { get; private set; }

    private readonly List<ShiftDay> _days = new();
    public IReadOnlyCollection<ShiftDay> Days => _days.AsReadOnly();

    private Shift() { }

    public static Shift Create(
        string name,
        TimeSpan startTime,
        int toleranceMinutes,
        TimeSpan workHours,
        ShiftType shiftType,
        IEnumerable<ShiftDay>? days = null)
    {
        var shift = new Shift
        {
            Id = ShiftId.CreateNew(),
            Name = name,
            StartTime = startTime,
            ToleranceMinutes = toleranceMinutes,
            WorkHours = workHours,
            ShiftType = shiftType,
            // Calculate EndTime logic: normalize to 24h day
            EndTime = NormalizeTime(startTime.Add(workHours))
        };

        if (days != null && days.Any())
        {
            shift._days.AddRange(days);
        }

        // validations?
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
        IEnumerable<ShiftDay>? days = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del turno es requerido.");
         if (toleranceMinutes < 0)
            throw new DomainException("El tiempo de tolerancia no puede ser negativo.");

        Name = name;
        StartTime = startTime;
        ToleranceMinutes = toleranceMinutes;
        WorkHours = workHours;
        ShiftType = shiftType;
        EndTime = NormalizeTime(startTime.Add(workHours));

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
