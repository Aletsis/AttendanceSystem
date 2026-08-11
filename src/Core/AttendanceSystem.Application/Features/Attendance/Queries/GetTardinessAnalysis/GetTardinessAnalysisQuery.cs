using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using System.Globalization;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetTardinessAnalysis;

public sealed record GetTardinessAnalysisQuery(
    DateTime StartDate, 
    DateTime EndDate, 
    BranchId? BranchId = null) : IRequest<Result<TardinessAnalysisDto>>;

public sealed class GetTardinessAnalysisQueryHandler 
    : IRequestHandler<GetTardinessAnalysisQuery, Result<TardinessAnalysisDto>>
{
    private readonly IDailyAttendanceRepository _dailyAttendanceRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IBranchRepository _branchRepository;

    public GetTardinessAnalysisQueryHandler(
        IDailyAttendanceRepository dailyAttendanceRepository,
        IEmployeeRepository employeeRepository,
        IDepartmentRepository departmentRepository,
        IBranchRepository branchRepository)
    {
        _dailyAttendanceRepository = dailyAttendanceRepository;
        _employeeRepository = employeeRepository;
        _departmentRepository = departmentRepository;
        _branchRepository = branchRepository;
    }

    public async Task<Result<TardinessAnalysisDto>> Handle(
        GetTardinessAnalysisQuery request, 
        CancellationToken cancellationToken)
    {
        try
        {
            var dailyRecords = await _dailyAttendanceRepository.GetByDateRangeAsync(
                request.StartDate, 
                request.EndDate, 
                request.BranchId, 
                null, 
                cancellationToken);

            var allEmployees = await _employeeRepository.GetAllAsync(cancellationToken);
            var employees = allEmployees.Where(e => e.Status == Domain.Enumerations.EmployeeStatus.Alta).ToList();
            if (request.BranchId != null)
            {
                employees = employees.Where(e => e.BranchId == request.BranchId).ToList();
            }
            
            var departments = await _departmentRepository.GetAllAsync(cancellationToken);
            var branches = await _branchRepository.GetAllAsync(cancellationToken);
            
            var deptDict = departments.ToDictionary(d => d.Id, d => d.Name);
            var branchDict = branches.ToDictionary(b => b.Id, b => b.Name);
            var empDict = employees.ToDictionary(e => e.Id, e => e);

            // Filtrar registros de empleados que ya no están activos o que no pertenecen a la sucursal (si no se filtró previamente)
            var validRecords = dailyRecords
                .Where(r => empDict.ContainsKey(r.EmployeeId))
                .ToList();

            var tardies = validRecords.Where(r => r.LateMinutes > 0 && !r.IsRestDay).ToList();
            var totalPossibleDays = validRecords.Count(r => !r.IsRestDay);

            if (totalPossibleDays == 0)
            {
                return Result<TardinessAnalysisDto>.Success(new TardinessAnalysisDto());
            }

            var totalTardinessMinutes = tardies.Sum(t => t.LateMinutes);

            // 1. Retardos por día de la semana
            var culture = new CultureInfo("es-MX");
            var tardinessByDay = tardies
                .GroupBy(a => a.Date.DayOfWeek)
                .Select(g => new DayTardinessDto(
                    culture.DateTimeFormat.GetDayName(g.Key),
                    g.Count(),
                    totalPossibleDays > 0 ? (double)g.Count() / totalPossibleDays * 100 : 0,
                    g.Sum(r => r.LateMinutes)
                ))
                .ToList();
            
            // Reordenar correctamente (patrón de lunes a domingo)
            var sortedDays = new List<string> { "lunes", "martes", "miércoles", "jueves", "viernes", "sábado", "domingo" };
            tardinessByDay = tardinessByDay
                .OrderBy(d => sortedDays.IndexOf(d.DayName.ToLower()))
                .ToList();

            // 2. Retardos por departamento
            var tardinessByDept = tardies
                .GroupBy(a => empDict[a.EmployeeId].DepartmentId)
                .Select(g => {
                    var deptName = deptDict.TryGetValue(g.Key, out var name) ? name : "Sin Departamento";
                    var possibleDaysInDept = validRecords
                        .Where(r => empDict[r.EmployeeId].DepartmentId == g.Key && !r.IsRestDay)
                        .Count();
                    
                    return new DepartmentTardinessDto(
                        deptName,
                        g.Count(),
                        employees.Count(e => e.DepartmentId == g.Key),
                        possibleDaysInDept > 0 ? (double)g.Count() / possibleDaysInDept * 100 : 0,
                        g.Sum(r => r.LateMinutes)
                    );
                })
                .OrderByDescending(d => d.Rate)
                .ToList();

            // 3. Empleados con más retardos
            var topEmployees = tardies
                .GroupBy(a => a.EmployeeId)
                .Select(g => {
                    var emp = empDict[g.Key];
                    var deptName = deptDict.TryGetValue(emp.DepartmentId, out var name) ? name : "N/A";
                    return new EmployeeTardinessDto(
                        $"{emp.FirstName} {emp.LastName}",
                        deptName,
                        g.Count(),
                        g.Sum(r => r.LateMinutes)
                    );
                })
                .OrderByDescending(e => e.TardyCount)
                .Take(10)
                .ToList();

            // 4. Retardos por sucursal
            var tardinessByBranch = tardies
                .GroupBy(a => empDict[a.EmployeeId].BranchId)
                .Select(g => {
                    var branchName = branchDict.TryGetValue(g.Key, out var name) ? name : "Sin Sucursal";
                    var possibleDaysInBranch = validRecords
                        .Where(r => empDict[r.EmployeeId].BranchId == g.Key && !r.IsRestDay)
                        .Count();
                    
                    return new BranchTardinessDto(
                        branchName,
                        g.Count(),
                        employees.Count(e => e.BranchId == g.Key),
                        possibleDaysInBranch > 0 ? (double)g.Count() / possibleDaysInBranch * 100 : 0,
                        g.Sum(r => r.LateMinutes)
                    );
                })
                .OrderByDescending(b => b.Rate)
                .ToList();

            // 5. Retardos por Sucursal/Departamento
            var tardinessByBranchDept = tardies
                .GroupBy(a => new { empDict[a.EmployeeId].BranchId, empDict[a.EmployeeId].DepartmentId })
                .Select(g => {
                    var branchName = branchDict.TryGetValue(g.Key.BranchId, out var bName) ? bName : "N/A";
                    var deptName = deptDict.TryGetValue(g.Key.DepartmentId, out var dName) ? dName : "N/A";
                    var possibleDaysInCombo = validRecords
                        .Where(r => empDict[r.EmployeeId].BranchId == g.Key.BranchId && 
                                   empDict[r.EmployeeId].DepartmentId == g.Key.DepartmentId && 
                                   !r.IsRestDay)
                        .Count();
                    
                    return new BranchDepartmentTardinessDto(
                        branchName,
                        deptName,
                        g.Count(),
                        possibleDaysInCombo > 0 ? (double)g.Count() / possibleDaysInCombo * 100 : 0,
                        g.Sum(r => r.LateMinutes)
                    );
                })
                .OrderByDescending(bd => bd.Rate)
                .ToList();

            var result = new TardinessAnalysisDto
            {
                TardinessRate = (double)tardies.Count / totalPossibleDays * 100,
                TotalTardies = tardies.Count,
                TotalPossibleWorkDays = totalPossibleDays,
                TotalTardinessMinutes = totalTardinessMinutes,
                TardinessByDayOfWeek = tardinessByDay,
                TardinessByDepartment = tardinessByDept,
                TardinessByBranch = tardinessByBranch,
                TardinessByBranchDepartment = tardinessByBranchDept,
                TopTardyEmployees = topEmployees
            };

            return Result<TardinessAnalysisDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<TardinessAnalysisDto>.Failure($"Error al analizar retardos: {ex.Message}");
        }
    }
}
