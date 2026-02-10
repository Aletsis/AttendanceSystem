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

        // Filter only active employees
        employees = employees.Where(e => e.Status == Domain.Enumerations.EmployeeStatus.Alta).ToList();


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

        // Logic for CheckIn/CheckOut strings
        string checkInStr = "--";
        if (att.ActualCheckIn.HasValue)
        {
             checkInStr = (att.ActualCheckIn.Value.Date == att.Date) 
                ? att.ActualCheckIn.Value.ToString("HH:mm:ss")
                : att.ActualCheckIn.Value.ToString("dd/MM/yyyy HH:mm:ss");
        }

        string checkOutStr = "--";
        if (att.ActualCheckOut.HasValue)
        {
             checkOutStr = (att.ActualCheckOut.Value.Date == att.Date) 
                ? att.ActualCheckOut.Value.ToString("HH:mm:ss")
                : att.ActualCheckOut.Value.ToString("dd/MM/yyyy HH:mm:ss");
        }

        return new AdvancedReportDetailDto
        {
            Date = att.Date,
            ShiftName = att.ShiftName ?? "",
            CheckIn = checkInStr,
            CheckOut = checkOutStr,
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
        double calculatedOvertime = 0;

        // Calculate dynamically to reflect "Worked vs Scheduled" logic even for historical data
        if (att.IsRestDay)
        {
            // On Rest Day, use schedule if available (e.g. newly created records), else fallback to 8h
            if (att.ActualCheckIn.HasValue && att.ActualCheckOut.HasValue)
            {
                var totalMinutes = (att.ActualCheckOut.Value - att.ActualCheckIn.Value).TotalMinutes;

                 if (att.ScheduledCheckIn.HasValue && att.ScheduledCheckOut.HasValue)
                {
                    var sIn = att.Date.Add(att.ScheduledCheckIn.Value);
                    var sOut = att.Date.Add(att.ScheduledCheckOut.Value);
                    if (att.ScheduledCheckOut < att.ScheduledCheckIn) sOut = sOut.AddDays(1);
                    
                    var sMinutes = (sOut - sIn).TotalMinutes;
                    calculatedOvertime = Math.Max(0, totalMinutes - sMinutes);
                }
                else
                {
                    calculatedOvertime = Math.Max(0, totalMinutes - 480);
                }
            }
        }
        else if (att.ScheduledCheckIn.HasValue && att.ScheduledCheckOut.HasValue && att.ActualCheckIn.HasValue && att.ActualCheckOut.HasValue)
        {
            var scheduledIn = att.Date.Add(att.ScheduledCheckIn.Value);
            var scheduledOut = att.Date.Add(att.ScheduledCheckOut.Value);
            if (att.ScheduledCheckOut < att.ScheduledCheckIn)
            {
                scheduledOut = scheduledOut.AddDays(1);
            }

            var scheduledDuration = (scheduledOut - scheduledIn).TotalMinutes;
            var workedDuration = (att.ActualCheckOut.Value - att.ActualCheckIn.Value).TotalMinutes;

            // Only calculate overtime if employee worked at least the scheduled hours
            if (workedDuration >= scheduledDuration)
            {
                // Calculate time from scheduled check-in to actual check-out
                var timeFromScheduledStart = (att.ActualCheckOut.Value - scheduledIn).TotalMinutes;
                
                // Overtime = Time from scheduled start to actual checkout - Scheduled duration
                calculatedOvertime = timeFromScheduledStart - scheduledDuration;
                if (calculatedOvertime < 0) calculatedOvertime = 0;
            }
        }
        else
        {
            // Fallback to stored value if schedule is missing or incomplete data
            calculatedOvertime = att.OvertimeMinutes;
        }

        if (emp.OvertimeCapType == Domain.Enumerations.OvertimeCapType.Daily && emp.OvertimeCapMinutes.HasValue)
        {
            return Math.Min(calculatedOvertime, emp.OvertimeCapMinutes.Value);
        }
        return calculatedOvertime;
    }
}
