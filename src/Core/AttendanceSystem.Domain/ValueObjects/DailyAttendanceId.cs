namespace AttendanceSystem.Domain.ValueObjects;

public sealed record DailyAttendanceId
{
    public Guid Value { get; }

    private DailyAttendanceId(Guid value)
    {
        Value = value;
    }

    public static DailyAttendanceId CreateUnique()
    {
        return new DailyAttendanceId(Guid.NewGuid());
    }

    public static DailyAttendanceId From(Guid value)
    {
        return new DailyAttendanceId(value);
    }
    
    public override string ToString() => Value.ToString();
}
