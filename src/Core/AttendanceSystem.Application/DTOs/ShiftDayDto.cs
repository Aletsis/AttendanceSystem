using System;

namespace AttendanceSystem.Application.DTOs;

public record ShiftDayDto(
    DayOfWeek DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan WorkHours
);
