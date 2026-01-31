using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AttendanceSystem.Application.Features.Reports.Queries.GetAdvancedAttendanceReport;

public class GetAdvancedAttendanceReportQueryHandler : IRequestHandler<GetAdvancedAttendanceReportQuery, IEnumerable<AdvancedReportSummaryDto>>
{
    private readonly IDailyAttendanceRepository _dailyAttendanceRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public GetAdvancedAttendanceReportQueryHandler(
        IDailyAttendanceRepository dailyAttendanceRepository,
        IEmployeeRepository employeeRepository,
        IDepartmentRepository departmentRepository)
    {
        _dailyAttendanceRepository = dailyAttendanceRepository;
        _employeeRepository = employeeRepository;
        _departmentRepository = departmentRepository;
    }

    public async Task<IEnumerable<AdvancedReportSummaryDto>> Handle(GetAdvancedAttendanceReportQuery request, CancellationToken cancellationToken)
    {
        // 1. Fetch Attendance Data
        var attendanceData = await _dailyAttendanceRepository.GetByDateRangeAsync(
            request.StartDate, 
            request.EndDate, 
            request.BranchId, 
            request.EmployeeId, 
            cancellationToken);

        // 2. Fetch Employees
        IReadOnlyList<Employee> employees;
        if (request.EmployeeId != null)
        {
            var emp = await _employeeRepository.GetByIdAsync(request.EmployeeId, cancellationToken);
            employees = emp != null ? new[] { emp } : Array.Empty<Employee>();
        }
        else if (request.BranchId != null)
        {
            employees = await _employeeRepository.GetByBranchAsync(request.BranchId, cancellationToken);
        }
        else
        {
            employees = await _employeeRepository.GetAllAsync(cancellationToken);
        }

        // 3. Fetch Departments for lookup
        var departments = await _departmentRepository.GetAllAsync(cancellationToken);
        var deptDict = departments.ToDictionary(d => d.Id, d => d.Name);

        var processed = new List<(Employee Emp, DailyAttendance Att)>();

        // 4. Filter Logic
        foreach (var item in attendanceData)
        {
            var emp = employees.FirstOrDefault(e => e.Id == item.EmployeeId);
            if (emp == null) continue;

            bool include = false;
            switch (request.ReportType)
            {
                case "Faltas":
                    include = item.IsAbsent;
                    break;
                case "DescansoTrabajado":
                    include = item.WorkedOnRestDay;
                    break;
                case "Retardos":
                    include = item.LateMinutes > 0;
                    break;
                case "HorasExtra":
                    include = item.OvertimeMinutes > 0;
                    break;
                case "HorarioErroneo":
                    if (item.ScheduledCheckIn.HasValue && item.ActualCheckIn.HasValue)
                    {
                        var scheduled = item.Date.Add(item.ScheduledCheckIn.Value);
                        var actual = item.ActualCheckIn.Value;
                        var diff = (actual - scheduled).TotalMinutes;
                        if (diff <= -25 || diff >= 16) include = true;
                    }
                    else if (item.ScheduledCheckIn.HasValue && !item.ActualCheckIn.HasValue && item.ActualCheckOut.HasValue)
                    {
                        include = true;
                    }
                    break;
                case "HorasLaboradas":
                    include = item.ActualCheckIn.HasValue && item.ActualCheckOut.HasValue;
                    break;
                case "DescansoErroneo":
                    include = true;
                    break;
                default:
                    include = true; 
                    break;
            }

            if (include)
            {
                processed.Add((emp, item));
            }
        }

        // 5. Grouping & Aggregation
        var grouped = processed.GroupBy(x => x.Emp.Id);
        var summaries = new List<AdvancedReportSummaryDto>();

        foreach (var g in grouped)
        {
            var empRef = g.First().Emp;
            var details = g.Select(x => x.Att).OrderBy(d => d.Date).ToList();

            // Descanso Erroneo Filter
            if (request.ReportType == "DescansoErroneo")
            {
                var empRecords = attendanceData.Where(r => r.EmployeeId == g.Key).ToList();
                bool hasWorkedRest = empRecords.Any(r => r.WorkedOnRestDay);
                bool hasAbsence = empRecords.Any(r => r.IsAbsent);

                if (!hasWorkedRest || !hasAbsence) continue;
            }
            
            var deptName = (empRef.DepartmentId != null && deptDict.TryGetValue(empRef.DepartmentId, out var dName)) ? dName : "";

            var summary = new AdvancedReportSummaryDto
            {
                EmployeeId = empRef.Id.Value,
                EmployeeName = empRef.GetFullName(),
                DepartmentName = deptName,
                Count = details.Count,
                Details = details.Select(d => MapToDetail(d, empRef)).ToList()
            };

            // Totals
            if (request.ReportType == "Retardos")
            {
                summary.TotalMetric = details.Sum(d => d.LateMinutes);
                summary.FormattedTotal = $"{summary.Count} Retardos ({summary.TotalMetric} min)";
            }
            else if (request.ReportType == "HorasExtra")
            {
                 // Calculate daily effective overtime first (handles Daily Cap)
                 double totalOvertime = details.Sum(d => GetEffectiveOvertime(d, empRef));

                 // Handle Period Cap
                 if (empRef.OvertimeCapType == Domain.Enumerations.OvertimeCapType.Period && empRef.OvertimeCapMinutes.HasValue)
                 {
                     totalOvertime = Math.Min(totalOvertime, empRef.OvertimeCapMinutes.Value);
                 }

                 summary.TotalMetric = totalOvertime;
                 summary.FormattedTotal = $"{summary.TotalMetric} min";
            }
            else if (request.ReportType == "HorasLaboradas")
            {
                double totalHours = 0;
                foreach(var d in details)
                {
                    if (d.ActualCheckIn.HasValue && d.ActualCheckOut.HasValue)
                    {
                        totalHours += (d.ActualCheckOut.Value - d.ActualCheckIn.Value).TotalHours;
                    }
                }
                summary.TotalMetric = totalHours;
                summary.FormattedTotal = $"{totalHours:F2} Horas";
            }
            else if (request.ReportType == "DescansoErroneo")
            {
                 summary.Count = 1; 
                 summary.FormattedTotal = "Incidencia Detectada";
            }
             else 
            {
                summary.FormattedTotal = $"{summary.Count} Eventos";
            }
            
            summaries.Add(summary);
        }

        return summaries.OrderBy(s => s.EmployeeName);
    }

    private AdvancedReportDetailDto MapToDetail(DailyAttendance att, Employee emp)
    {
        var workedVal = (att.ActualCheckOut - att.ActualCheckIn);
        string workedStr = workedVal.HasValue ? $"{(int)workedVal.Value.TotalHours:00}:{workedVal.Value.Minutes:00}" : "--:--";

        double effectiveOvertime = GetEffectiveOvertime(att, emp);

        return new AdvancedReportDetailDto
        {
            Date = att.Date,
            ShiftName = att.ShiftName ?? "",
            CheckIn = att.ActualCheckIn?.ToString("HH:mm:ss") ?? "--",
            CheckOut = att.ActualCheckOut?.ToString("HH:mm:ss") ?? "--",
            LateMinutes = att.LateMinutes,
            OvertimeMinutes = effectiveOvertime,
            WorkedHours = workedStr,
            IsAbsent = att.IsAbsent,
            WorkedOnRestDay = att.WorkedOnRestDay,
            IsRestDay = att.IsRestDay,
            DailyAttendanceId = att.Id.Value
        };
    }

    private double GetEffectiveOvertime(DailyAttendance att, Employee emp)
    {
        if (emp.OvertimeCapType == Domain.Enumerations.OvertimeCapType.Daily && emp.OvertimeCapMinutes.HasValue)
        {
            return Math.Min(att.OvertimeMinutes, emp.OvertimeCapMinutes.Value);
        }
        return att.OvertimeMinutes;
    }
}
