using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.DTOs;

public record ShiftRosterDto(
    Guid Id,
    string EmployeeId,
    string EmployeeName,
    DateTime Date,
    Guid? ShiftId,
    string? ShiftName,
    ShiftType? ShiftType,
    TimeSpan? StartTime,
    TimeSpan? EndTime,
    bool IsRestDay,
    string? Notes);

public enum RotationSchemeType
{
    Rotativo3x8 = 0,
    Esquema4x3 = 1,
    Esquema24x48 = 2,
    Personalizado = 3,
    Rotativo2x8 = 4
}
