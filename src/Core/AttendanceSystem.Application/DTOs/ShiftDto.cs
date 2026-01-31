using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.DTOs;

public record ShiftDto(
    Guid Id,
    string Name,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int ToleranceMinutes,
    TimeSpan WorkHours,
    ShiftType ShiftType
);
