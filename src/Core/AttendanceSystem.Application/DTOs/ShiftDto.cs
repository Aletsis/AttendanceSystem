using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.DTOs;

public record ShiftDto(
    Guid Id,
    string Name,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int ToleranceMinutes,
    TimeSpan WorkHours,
    ShiftType ShiftType,
    IEnumerable<ShiftDayDto> Days,
    bool RoundingsEnabled,
    int RoundingInterval
)
{
    public ShiftDto() : this(Guid.Empty, string.Empty, TimeSpan.Zero, TimeSpan.Zero, 0, TimeSpan.Zero, ShiftType.Matutino, Enumerable.Empty<ShiftDayDto>(), false, 0) { }
}
