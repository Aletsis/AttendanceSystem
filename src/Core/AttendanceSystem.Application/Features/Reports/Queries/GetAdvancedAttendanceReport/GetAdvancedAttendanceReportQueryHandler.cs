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
        // 1. Obtener datos de asistencia
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

        // Filtrar solo empleados activos
        employees = employees.Where(e => e.Status == Domain.Enumerations.EmployeeStatus.Alta).ToList();


        // 3. Obtener Departamentos, Puestos y Sucursales para referencia
        var departments = await _departmentRepository.GetAllAsync(cancellationToken);
        var deptDict = departments.ToDictionary(d => d.Id, d => d.Name);

        var positions = await _positionRepository.GetAllAsync(cancellationToken);
        var posDict = positions.ToDictionary(p => p.Id, p => p.Name);

        var branches = await _branchRepository.GetAllAsync(cancellationToken);
        var branchDict = branches.ToDictionary(b => b.Id, b => b.Name);

        var processed = new List<(Employee Emp, DailyAttendance Att)>();

        // 4. Logica de filtrado
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

        // 5. Agrupar y resumir por empleado
        var grouped = processed.GroupBy(x => x.Emp.Id);
        var summaries = new List<AdvancedReportSummaryDto>();

        foreach (var g in grouped)
        {
            var empRef = g.First().Emp;
            var details = g.Select(x => x.Att).OrderBy(d => d.Date).ToList();

            // Filtro para "DescansoErroneo": Solo incluir empleados que tengan al menos un registro de trabajo en día de descanso y al menos una falta.
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

            // Totales
            if (request.ReportType == "Retardos")
            {
                summary.TotalMetric = details.Sum(d => d.LateMinutes);
                summary.FormattedTotal = $"{summary.Count} Retardos ({summary.TotalMetric} min)";
            }
            else if (request.ReportType == "HorasExtra" || request.ReportType == "HorasExtraPorDepartamento")
            {
                 // Calcular primero el tiempo extra efectivo diario (maneja el límite diario)
                 double totalOvertime = details.Sum(d => GetEffectiveOvertime(d, empRef));

                  // Calcular primero el tiempo trabajado total (para mostrarlo en el resumen)
                  double totalWorkedHours = 0;
                  foreach(var d in details)
                  {
                     var refIn = GetReferenceEntry(d);
                     var refOut = GetReferenceExit(d);
                     if (refIn.HasValue && refOut.HasValue)
                     {
                         totalWorkedHours += (refOut.Value - refIn.Value).TotalHours;
                     }
                  }

                 // Maneja el límite de tiempo extra por periodo si aplica
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
        DateTime? referenceExit = GetReferenceExit(att);

        // 2. Duración trabajada basada en la regla del usuario
        TimeSpan? workedVal = (referenceExit.HasValue && referenceEntry.HasValue) 
            ? (referenceExit.Value - referenceEntry.Value) 
            : null;
            
        string workedStr = workedVal.HasValue ? $"{(int)workedVal.Value.TotalHours:00}:{workedVal.Value.Minutes:00}" : "--:--";

        double effectiveOvertime = GetEffectiveOvertime(att, emp);

        // Logica para las cadenas de CheckIn/CheckOut
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

        // 1. PRIORIDAD A LOS DATOS ALMACENADOS: Si el registro ha sido procesado y tiene tiempo extra, usarlo.
        // Este paso asegura que las modificaciones manuales y los datos reprocesados se reflejen.
        if (att.OvertimeMinutes > 0)
        {
            calculatedOvertime = att.OvertimeMinutes;
        }
        else
        {
            // 2. FALLBACK/RECALCULATION: Si el almacenado es 0 pero tenemos registros, verificar si hay tiempo extra
            // (Este paso maneja días de descanso con nueva lógica o registros que aún no se han reprocesado completamente)
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
                DateTime referenceExit = GetReferenceExit(att) ?? att.ActualCheckOut.Value;
                var workedDuration = (referenceExit - referenceEntry).TotalMinutes;
                calculatedOvertime = workedDuration - goal;
                if (calculatedOvertime < 0) calculatedOvertime = 0;
            }
            else
            {
                 calculatedOvertime = att.OvertimeMinutes;
            }
        }

        if (emp.OvertimeCapType == Domain.Enumerations.OvertimeCapType.Daily && emp.OvertimeCapMinutes.HasValue)
        {
            calculatedOvertime = Math.Min(calculatedOvertime, emp.OvertimeCapMinutes.Value);
        }

        // Aplicar redondeo según la configuración del empleado
        calculatedOvertime = ApplyOvertimeRounding(calculatedOvertime, emp.OvertimeCalculationMethod);

        return calculatedOvertime;
    }

    private DateTime? GetReferenceEntry(DailyAttendance att)
    {
        return att.GetReferenceEntry();
    }

    private DateTime? GetReferenceExit(DailyAttendance att)
    {
        return att.GetReferenceExit();
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
