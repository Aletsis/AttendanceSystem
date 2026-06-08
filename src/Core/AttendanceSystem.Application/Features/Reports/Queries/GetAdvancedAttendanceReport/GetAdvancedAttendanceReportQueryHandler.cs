using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Enumerations;
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
    private readonly IPositionRepository _positionRepository;
    private readonly IBranchRepository _branchRepository;

    public GetAdvancedAttendanceReportQueryHandler(
        IDailyAttendanceRepository dailyAttendanceRepository,
        IEmployeeRepository employeeRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository, 
        IBranchRepository branchRepository)
    {
        _dailyAttendanceRepository = dailyAttendanceRepository;
        _employeeRepository = employeeRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _branchRepository = branchRepository;
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

        if (request.DepartmentId != null)
        {
            employees = employees.Where(e => e.DepartmentId == request.DepartmentId).ToList();
        }

        // Filter only active employees
        employees = employees.Where(e => e.Status == Domain.Enumerations.EmployeeStatus.Alta).ToList();


        // 3. Fetch Departments, Positions, Branches for lookup
        var departments = await _departmentRepository.GetAllAsync(cancellationToken);
        var deptDict = departments.ToDictionary(d => d.Id, d => d.Name);

        var positions = await _positionRepository.GetAllAsync(cancellationToken);
        var posDict = positions.ToDictionary(p => p.Id, p => p.Name);

        var branches = await _branchRepository.GetAllAsync(cancellationToken);
        var branchDict = branches.ToDictionary(b => b.Id, b => b.Name);

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
                    include = item.ActualCheckIn.HasValue && item.ActualCheckOut.HasValue;
                    break;
                case "HorasExtraPorDepartamento":
                    include = item.ActualCheckIn.HasValue && item.ActualCheckOut.HasValue;
                    break;
                case "HorarioErroneo":
                    if (item.ScheduledCheckIn.HasValue && item.ActualCheckIn.HasValue && item.ActualCheckOut.HasValue)
                    {
                        var scheduled = item.Date.Add(item.ScheduledCheckIn.Value);
                        var actual = item.ActualCheckIn.Value;
                        var diff = (actual - scheduled).TotalMinutes;
                        if (diff <= -25 || diff >= 16) include = true;
                    }
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
            var posName = (empRef.PositionId != null && posDict.TryGetValue(empRef.PositionId, out var pName)) ? pName : "";
            var branchName = (empRef.BranchId != null && branchDict.TryGetValue(empRef.BranchId, out var bName)) ? bName : "";

            var summary = new AdvancedReportSummaryDto
            {
                EmployeeId = string.IsNullOrEmpty(empRef.Id.Value) ? "" : empRef.Id.Value.PadLeft(4, '0'),
                EmployeeName = empRef.GetFullName(),
                DepartmentName = deptName,
                PositionName = posName,
                BranchName = branchName,
                Count = details.Count,
                Details = details.Select(d => MapToDetail(d, empRef)).ToList()
            };

            // Totals
            if (request.ReportType == "Retardos")
            {
                summary.TotalMetric = details.Sum(d => d.LateMinutes);
                summary.FormattedTotal = $"{summary.Count} Retardos ({summary.TotalMetric} min)";
            }
            else if (request.ReportType == "HorasExtra" || request.ReportType == "HorasExtraPorDepartamento")
            {
                 // Calculate daily effective overtime first (handles Daily Cap)
                 double totalOvertime = details.Sum(d => GetEffectiveOvertime(d, empRef));

                 // Calculate total worked hours
                 double totalWorkedHours = 0;
                 foreach(var d in details)
                 {
                    if (d.ActualCheckIn.HasValue && d.ActualCheckOut.HasValue)
                    {
                        totalWorkedHours += (d.ActualCheckOut.Value - d.ActualCheckIn.Value).TotalHours;
                    }
                 }

                 // Handle Period Cap
                 if (empRef.OvertimeCapType == OvertimeCapType.Period && empRef.OvertimeCapMinutes.HasValue)
                 {
                     totalOvertime = Math.Min(totalOvertime, empRef.OvertimeCapMinutes.Value);
                 }

                 summary.TotalMetric = totalOvertime;
                 var otSpan = TimeSpan.FromMinutes(totalOvertime);
                 summary.FormattedTotal = $"Lab: {totalWorkedHours:F1}h | Ext: {(int)otSpan.TotalHours:00}:{otSpan.Minutes:00}";
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

        if (request.ReportType == "HorasExtraPorDepartamento")
        {
            return summaries.OrderBy(s => s.DepartmentName)
                            .ThenBy(s => int.TryParse(s.EmployeeId, out int id) ? id : int.MaxValue)
                            .ThenBy(s => s.EmployeeId);
        }

        return summaries.OrderBy(s => int.TryParse(s.EmployeeId, out int id) ? id : int.MaxValue)
                        .ThenBy(s => s.EmployeeId);
    }

    private AdvancedReportDetailDto MapToDetail(DailyAttendance att, Employee emp)
    {
        // 1. Determine Reference Entry (Entrada de Referencia)
        DateTime? referenceEntry = GetReferenceEntry(att);


        // 2. Worked Duration based on the user's rule
        TimeSpan? workedVal = (att.ActualCheckOut.HasValue && referenceEntry.HasValue) 
            ? (att.ActualCheckOut.Value - referenceEntry.Value) 
            : null;
            
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
        // 1. PRIORITIZE STORED DATA: If the record has been processed and has overtime, use it.
        // This ensures manual modifications and reprocessed data are reflected.
        if (att.OvertimeMinutes > 0)
        {
            return att.OvertimeMinutes;
        }

        // 2. FALLBACK/RECALCULATION: If stored is 0 but we have logs, check if there's extra
        // (This handles rest days with new logic or records not yet fully reprocessed)
        double calculatedOvertime = 0;
        double goal = 0;
        
        if (!att.IsRestDay && att.ScheduledCheckIn.HasValue && att.ScheduledCheckOut.HasValue)
        {
            var sIn = att.Date.Add(att.ScheduledCheckIn.Value);
            var sOut = att.Date.Add(att.ScheduledCheckOut.Value);
            if (att.ScheduledCheckOut < att.ScheduledCheckIn) sOut = sOut.AddDays(1);
            goal = (sOut - sIn).TotalMinutes;
        }
        else if (!att.IsRestDay)
        {
            goal = 480; 
        }

        if (att.ActualCheckIn.HasValue && att.ActualCheckOut.HasValue)
        {
            DateTime referenceEntry = GetReferenceEntry(att) ?? att.ActualCheckIn.Value;
            var workedDuration = (att.ActualCheckOut.Value - referenceEntry).TotalMinutes;
            calculatedOvertime = workedDuration - goal;
            if (calculatedOvertime < 0) calculatedOvertime = 0;
        }
        else
        {
             calculatedOvertime = att.OvertimeMinutes;
        }

        if (emp.OvertimeCapType == Domain.Enumerations.OvertimeCapType.Daily && emp.OvertimeCapMinutes.HasValue)
        {
            calculatedOvertime = Math.Min(calculatedOvertime, emp.OvertimeCapMinutes.Value);
        }

        // Apply Rounding
        calculatedOvertime = ApplyOvertimeRounding(calculatedOvertime, emp.OvertimeCalculationMethod);

        return calculatedOvertime;
    }

    private DateTime? GetReferenceEntry(DailyAttendance att)
    {
        if (!att.ActualCheckIn.HasValue) return null;
        
        DateTime referenceEntry = att.ActualCheckIn.Value;
        
        if (att.ShiftType != ShiftType.Continuo && att.ScheduledCheckIn.HasValue)
        {
            var sIn = att.Date.Add(att.ScheduledCheckIn.Value);
            var delayMinutes = (att.ActualCheckIn.Value - sIn).TotalMinutes;
            
            if (delayMinutes > att.ToleranceMinutes)
            {
                // Lateness exceeding tolerance -> round up to next 30-minute block from scheduled start, giving tolerance in each block
                double rawK = (delayMinutes - att.ToleranceMinutes) / 30.0;
                int k = (int)Math.Ceiling(rawK);
                if (k < 0) k = 0;
                
                referenceEntry = sIn.AddMinutes(k * 30);
            }
            else
            {
                if (att.CalculateOvertimeBeforeEntry)
                {
                    referenceEntry = att.ActualCheckIn.Value;
                }
                else
                {
                    referenceEntry = sIn;
                }
            }
        }
        
        return referenceEntry;
    }

    private double ApplyOvertimeRounding(double minutes, OvertimeCalculationMethod method)
    {
        switch (method)
        {
            case OvertimeCalculationMethod.RoundByHalfHour:
                return Math.Floor(minutes / 30.0) * 30.0;
            case OvertimeCalculationMethod.RoundByHour:
                return Math.Floor(minutes / 60.0) * 60.0;
            default:
                return minutes;
        }
    }
}
