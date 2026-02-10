using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;

public class GetAttendanceReportQueryHandler : IRequestHandler<GetAttendanceReportQuery, IEnumerable<AttendanceReportViewDto>>
{
    private readonly IDailyAttendanceRepository _dailyAttendanceRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;

    public GetAttendanceReportQueryHandler(
        IDailyAttendanceRepository dailyAttendanceRepository,
        IEmployeeRepository employeeRepository,
        IBranchRepository branchRepository)
    {
        _dailyAttendanceRepository = dailyAttendanceRepository;
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
    }

    public async Task<IEnumerable<AttendanceReportViewDto>> Handle(GetAttendanceReportQuery request, CancellationToken cancellationToken)
    {
        EmployeeId? empId = !string.IsNullOrEmpty(request.EmployeeId) ? EmployeeId.From(request.EmployeeId) : null;

        // 1. Fetch Processed Attendance
        var attendanceData = await _dailyAttendanceRepository.GetByDateRangeAsync(
            request.StartDate, 
            request.EndDate, 
            request.BranchId, 
            empId, 
            cancellationToken);

        // 2. Fetch Metadata
        // 2. Fetch Metadata
        var allEmployees = await _employeeRepository.GetAllAsync(cancellationToken);
        var employees = allEmployees.Where(e => e.Status == Domain.Enumerations.EmployeeStatus.Alta).ToList();
        var branches = await _branchRepository.GetAllAsync(cancellationToken);
        
        var empDict = employees.ToDictionary(e => e.Id, e => e);
        var branchDict = branches.ToDictionary(b => b.Id, b => b.Name);

        // 3. Map to DTO
        var dtos = new List<AttendanceReportViewDto>();
        
        // Group by Employee to handle period sorting/caps
        var attByEmployee = attendanceData.GroupBy(a => a.EmployeeId);

        foreach (var group in attByEmployee)
        {
            var emp = empDict.TryGetValue(group.Key, out var e) ? e : null;
            if (emp == null) continue;

            string empName = emp.GetFullName();

            string branchName = "N/A";
            if (emp != null && emp.BranchId != null && branchDict.TryGetValue(emp.BranchId, out var bName))
            {
                branchName = bName;
            }
            else if (request.BranchId != null && branchDict.TryGetValue(request.BranchId, out var reqBranchName))
            {
                branchName = reqBranchName;
            }

            // Calculation Logic for Overtime Cap
            double accumulatedOvertime = 0;
            double? periodCap = (emp?.OvertimeCapType == Domain.Enumerations.OvertimeCapType.Period) ? emp.OvertimeCapMinutes : null;
            double? dailyCap = (emp?.OvertimeCapType == Domain.Enumerations.OvertimeCapType.Daily) ? emp.OvertimeCapMinutes : null;

            var sortedRecords = group.OrderBy(x => x.Date).ToList();

            foreach (var att in sortedRecords)
            {
                double effectiveOvertime = att.OvertimeMinutes;

                // Daily Cap
                if (dailyCap.HasValue && dailyCap.Value >= 0)
                {
                    effectiveOvertime = Math.Min(effectiveOvertime, dailyCap.Value);
                }

                // Period Cap
                if (periodCap.HasValue && periodCap.Value >= 0)
                {
                    double remaining = Math.Max(0, periodCap.Value - accumulatedOvertime);
                    effectiveOvertime = Math.Min(effectiveOvertime, remaining);
                    
                    accumulatedOvertime += effectiveOvertime;
                }

                dtos.Add(new AttendanceReportViewDto
                {
                    EmployeeId = att.EmployeeId.Value,
                    EmployeeName = empName,
                    Date = att.Date,
                    ShiftName = att.ShiftName ?? "",
                    BranchName = branchName,
                    ScheduledCheckIn = att.ScheduledCheckIn,
                    ScheduledCheckOut = att.ScheduledCheckOut,
                    ActualCheckIn = att.ActualCheckIn,
                    ActualCheckOut = att.ActualCheckOut,
                    LateMinutes = att.LateMinutes,
                    OvertimeMinutes = (int)effectiveOvertime,
                    IsAbsent = att.IsAbsent,
                    IsRestDay = att.IsRestDay,
                    WorkedOnRestDay = att.WorkedOnRestDay,
                    MissingCheckIn = att.MissingCheckIn,
                    MissingCheckOut = att.MissingCheckOut,
                    OvertimeCalculationMethod = emp?.OvertimeCalculationMethod ?? Domain.Enumerations.OvertimeCalculationMethod.NoRounding
                });
            }
        }

        // Default Sort
        return dtos.OrderBy(x => x.EmployeeId)
                   .ThenBy(x => x.Date);
    }
}
