using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Domain.Aggregates.ShiftAggregate;

public class Shift : AggregateRoot<ShiftId>
{
    public string Name { get; private set; }
    public TimeSpan StartTime { get; private set; }
    public TimeSpan EndTime { get; private set; } // Calculated or explicit? Usually Start + WorkHours.
    public int ToleranceMinutes { get; private set; }
    public TimeSpan WorkHours { get; private set; }
    public ShiftType ShiftType { get; private set; }

    private Shift() { }

    public static Shift Create(
        string name,
        TimeSpan startTime,
        int toleranceMinutes,
        TimeSpan workHours,
        ShiftType shiftType)
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
        ShiftType shiftType)
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
    }

    private static TimeSpan NormalizeTime(TimeSpan time)
    {
        return time.TotalDays >= 1 
            ? time.Subtract(TimeSpan.FromDays((int)time.TotalDays)) 
            : time;
    }
}
