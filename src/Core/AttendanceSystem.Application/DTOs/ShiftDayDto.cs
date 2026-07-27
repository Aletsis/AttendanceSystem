using System;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.DTOs;

public record ShiftDayDto(
    DayOfWeek DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan WorkHours,
    ShiftType ShiftType
);
