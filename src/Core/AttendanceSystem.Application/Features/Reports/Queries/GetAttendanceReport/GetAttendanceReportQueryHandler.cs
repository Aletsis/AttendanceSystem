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
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly ISystemConfigurationRepository _systemConfigurationRepository;

    public GetAttendanceReportQueryHandler(
        IDailyAttendanceRepository dailyAttendanceRepository,
        IEmployeeRepository employeeRepository,
        IBranchRepository branchRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository,
        ISystemConfigurationRepository systemConfigurationRepository)
    {
        _dailyAttendanceRepository = dailyAttendanceRepository;
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _systemConfigurationRepository = systemConfigurationRepository;
    }

    public async Task<IEnumerable<AttendanceReportViewDto>> Handle(GetAttendanceReportQuery request, CancellationToken cancellationToken)
    {
        EmployeeId? empId = !string.IsNullOrEmpty(request.EmployeeId) ? EmployeeId.From(request.EmployeeId) : null;

        // 1. Obtener Configuración del Sistema para determinar el día de inicio de semana
        var config = await _systemConfigurationRepository.GetConfigurationAsync(cancellationToken);
        var weeklyStartDay = config?.WeeklyStartDay ?? DayOfWeek.Monday;

        // Determinar la fecha de inicio de la semana de corte más temprana para calcular fielmente la bolsa semanal
        var earliestWeekStart = Common.WeeklyHoursCalculationHelper.GetWeekStart(request.StartDate, weeklyStartDay);

        // 2. Obtener Asistencia Procesada desde el inicio de la semana para acumulados semanales fiables
        var attendanceData = await _dailyAttendanceRepository.GetByDateRangeAsync(
            earliestWeekStart,
            request.EndDate,
            request.BranchId,
            empId,
            cancellationToken);

        // 3. Obtener Empleados, Sucursales, Departamentos y Puestos
        var allEmployees = await _employeeRepository.GetAllAsync(cancellationToken);
        var employees = allEmployees.Where(e => e.Status == Domain.Enumerations.EmployeeStatus.Alta).ToList();
        var branches = await _branchRepository.GetAllAsync(cancellationToken);
        var departments = await _departmentRepository.GetAllAsync(cancellationToken);
        var positions = await _positionRepository.GetAllAsync(cancellationToken);

        var empDict = employees.ToDictionary(e => e.Id, e => e);
        var branchDict = branches.ToDictionary(b => b.Id, b => b.Name);
        var deptDict = departments.ToDictionary(d => d.Id, d => d.Name);
        var posDict = positions.ToDictionary(p => p.Id, p => p.Name);

        // 4. Mapear a DTO
        var dtos = new List<AttendanceReportViewDto>();

        // Agrupar por empleado para calcular acumulados semanales y límites
        var attByEmployee = attendanceData.GroupBy(a => a.EmployeeId);

        foreach (var group in attByEmployee)
        {
            if (!empDict.TryGetValue(group.Key, out var emp) || emp == null) continue;

            string empName = emp.GetFullName();

            string branchName = "N/A";
            if (emp.BranchId != null && branchDict.TryGetValue(emp.BranchId, out var bName))
            {
                branchName = bName;
            }
            else if (request.BranchId != null && branchDict.TryGetValue(request.BranchId, out var reqBranchName))
            {
                branchName = reqBranchName;
            }

            var sortedRecords = group.OrderBy(x => x.Date).ToList();

            foreach (var att in sortedRecords)
            {
                // F06-08: fuera de un período de empleo (antes del ingreso o durante una baja) no se reporta
                if (!emp.IsActiveOn(att.Date)) continue;

                // Solo incluir en el reporte los registros que caen dentro del rango solicitado
                if (att.Date.Date < request.StartDate.Date || att.Date.Date > request.EndDate.Date) continue;

                // F06-09: las extras efectivas las calcula y guarda el motor (autorización diaria, redondeo y topes por
                // período); el reporte no las reinterpreta con la configuración actual ni con el rango consultado.
                int effectiveOvertime = att.OvertimeMinutes;

                dtos.Add(new AttendanceReportViewDto
                {
                    EmployeeId = att.EmployeeId.Value,
                    EmployeeName = empName,
                    Date = att.Date,
                    ShiftName = att.ShiftName ?? "",
                    BranchName = branchName,
                    DepartmentName = (emp.DepartmentId != null && deptDict.TryGetValue(emp.DepartmentId, out var dName)) ? dName : "N/A",
                    PositionName = (emp.PositionId != null && posDict.TryGetValue(emp.PositionId, out var pName)) ? pName : "N/A",
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
                    // --- Turnos Partidos ---
                    ShiftType = att.ShiftType,
                    ScheduledBlock2CheckIn = att.ScheduledBlock2CheckIn,
                    ScheduledBlock2CheckOut = att.ScheduledBlock2CheckOut,
                    ActualBlock1CheckOut = att.ActualBlock1CheckOut,
                    ActualBlock2CheckIn = att.ActualBlock2CheckIn,
                    MissingBlock1CheckOut = att.MissingBlock1CheckOut,
                    MissingBlock2CheckIn = att.MissingBlock2CheckIn,
                    OvertimeCalculationMethod = emp.OvertimeCalculationMethod,
                    // --- Salidas Temporales ---
                    HasTemporaryExits = att.HasTemporaryExits,
                    TemporaryExitMinutes = att.TemporaryExitMinutes,
                    TemporaryExitStatus = (int)att.TemporaryExitStatus,
                    AttendanceNote = att.AttendanceNote,
                    TotalWorkedMinutes = att.TotalWorkedMinutes,
                    PunchTrackingMode = att.PunchTrackingMode,
                    IntervalsData = att.IntervalsData
                });
            }
        }

        // Ordenar por EmployeeId y luego por Date
        return dtos.OrderBy(x => x.EmployeeId)
                   .ThenBy(x => x.Date);
    }
}
