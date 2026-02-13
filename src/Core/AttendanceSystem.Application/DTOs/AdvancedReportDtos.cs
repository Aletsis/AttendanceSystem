using System;
using System.Collections.Generic;

namespace AttendanceSystem.Application.DTOs;

public sealed record AdvancedReportSummaryDto
{
    public string EmployeeId { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public string PositionName { get; init; } = string.Empty;
    public string BranchName { get; init; } = string.Empty;
    public int Count { get; set; }
    public double TotalMetric { get; set; }
    public string FormattedTotal { get; set; } = string.Empty;
    public List<AdvancedReportDetailDto> Details { get; init; } = new();
}

public sealed record AdvancedReportDetailDto
{
    public DateTime Date { get; init; }
    public string ShiftName { get; init; } = string.Empty;
    public string CheckIn { get; init; } = "--";
    public string CheckOut { get; init; } = "--";
    public double LateMinutes { get; init; }
    public double OvertimeMinutes { get; init; }
    public string WorkedHours { get; init; } = "--:--";
    
    public bool IsAbsent { get; init; }
    public bool WorkedOnRestDay { get; init; }
    public bool IsRestDay { get; init; }
    public Guid? DailyAttendanceId { get; init; }
}
