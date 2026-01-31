using System;
using System.Collections.Generic;

namespace AttendanceSystem.Blazor.Server.Components.Pages.ViewModels;

public class ReportItem
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public string CheckIn { get; set; } = "--";
    public string CheckOut { get; set; } = "--";
    public int LateMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public string WorkedHeaders { get; set; } = "--:--"; 
}

public class ReportSummaryItem
{
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public int Count { get; set; }
    public double TotalMetric { get; set; } 
    public string FormattedTotal { get; set; } = string.Empty;
    public List<ReportItem> Details { get; set; } = new();
}
