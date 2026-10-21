using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;

/// <summary>
/// Marcajes propios de una jornada: los de su fecha laboral más su entrada anticipada, que puede caer el día
/// anterior cuando la jornada empieza cerca de medianoche (decisión de cierre F06-01: desde 2 h antes).
/// </summary>
internal static class JornadaDay
{
    public static bool Contains(AttendanceRecord record, DateOnly day, string? timeZoneId, DateTime? earlyEntryStart)
    {
        var workDate = record.GetWorkDate(timeZoneId);
        if (workDate == day) return true;
        return earlyEntryStart is { } start &&
               workDate == day.AddDays(-1) &&
               record.GetCivilTime(timeZoneId) >= start;
    }
}
