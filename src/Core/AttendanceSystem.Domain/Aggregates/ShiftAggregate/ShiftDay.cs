using System;

namespace AttendanceSystem.Domain.Aggregates.ShiftAggregate;

public record ShiftDay
{
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeSpan StartTime { get; private set; }
    public TimeSpan EndTime { get; private set; }
    public TimeSpan WorkHours { get; private set; }

    private ShiftDay() { } // Para EF Core

    public ShiftDay(DayOfWeek dayOfWeek, TimeSpan startTime, TimeSpan workHours)
    {
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        WorkHours = workHours;
        EndTime = NormalizeTime(startTime.Add(workHours));
    }

    private static TimeSpan NormalizeTime(TimeSpan time)
    {
        return time.TotalDays >= 1 
            ? time.Subtract(TimeSpan.FromDays((int)time.TotalDays)) 
            : time;
    }
}
