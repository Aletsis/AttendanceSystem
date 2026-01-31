using System;

namespace AttendanceSystem.Application.DTOs;

public sealed record AttendanceReportViewDto
{
    public string EmployeeId { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public string ShiftName { get; init; } = string.Empty;
    public string BranchName { get; init; } = string.Empty;
    public TimeSpan? ScheduledCheckIn { get; init; }
    public TimeSpan? ScheduledCheckOut { get; init; }
    public DateTime? ActualCheckIn { get; init; }
    public DateTime? ActualCheckOut { get; init; }
    public int LateMinutes { get; init; }
    public int OvertimeMinutes { get; init; }
    
    public bool IsAbsent { get; init; }
    public bool IsRestDay { get; init; }
    public bool WorkedOnRestDay { get; init; }
    public bool MissingCheckIn { get; init; }
    public bool MissingCheckOut { get; init; }
    
    public AttendanceSystem.Domain.Enumerations.OvertimeCalculationMethod OvertimeCalculationMethod { get; init; }

    public int RoundedOvertimeMinutes
    {
        get
        {
            if (OvertimeMinutes <= 0) return 0;

            int minutes = OvertimeMinutes;
            switch (OvertimeCalculationMethod)
            {
                case AttendanceSystem.Domain.Enumerations.OvertimeCalculationMethod.RoundByHalfHour:
                    minutes = (minutes / 30) * 30;
                    break;
                case AttendanceSystem.Domain.Enumerations.OvertimeCalculationMethod.RoundByHour:
                    minutes = (minutes / 60) * 60;
                    break;
            }
            return Math.Max(0, minutes);
        }
    }
}
