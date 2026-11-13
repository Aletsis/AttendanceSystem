using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetAttendanceLogs;

public class GetAttendanceLogsQueryHandler : IRequestHandler<GetAttendanceLogsQuery, IEnumerable<AttendanceLogViewDto>>
{
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IDailyAttendanceRepository _dailyAttendanceRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IShiftRepository? _shiftRepository;
    private readonly IAttendanceAdjustmentRepository? _adjustmentRepository;

    public GetAttendanceLogsQueryHandler(
        IAttendanceRepository attendanceRepository,
        IDailyAttendanceRepository dailyAttendanceRepository,
        IEmployeeRepository employeeRepository,
        IDeviceRepository deviceRepository,
        IShiftRepository? shiftRepository = null,
        IAttendanceAdjustmentRepository? adjustmentRepository = null)
    {
        _attendanceRepository = attendanceRepository;
        _dailyAttendanceRepository = dailyAttendanceRepository;
        _employeeRepository = employeeRepository;
        _deviceRepository = deviceRepository;
        _shiftRepository = shiftRepository;
        _adjustmentRepository = adjustmentRepository;
    }

    public async Task<IEnumerable<AttendanceLogViewDto>> Handle(GetAttendanceLogsQuery request, CancellationToken cancellationToken)
    {
        var dateOnly = DateOnly.FromDateTime(request.Date);
        EmployeeId? empId = !string.IsNullOrEmpty(request.EmployeeId) ? EmployeeId.From(request.EmployeeId) : null;

        // 1. Obtener los registros de asistencia crudos para la fecha y empleado especificados
        var rawRecords = await _attendanceRepository.GetByDateRangeAsync(
            dateOnly,
            dateOnly,
            empId,
            cancellationToken);

        // 2. Obtener los registros de asistencia procesados para la fecha y empleado especificados
        // Extendemos el rango a Date-1 y Date+1 para capturar asignaciones que cruzan días (por ejemplo, la salida del lunes que ocurre el martes por la mañana).
        var processed = await _dailyAttendanceRepository.GetByDateRangeAsync(
            request.Date.Date.AddDays(-1),
            request.Date.Date.AddDays(1),
            null, // Branch
            empId,
            cancellationToken);

        // 3. Buscar empleados y dispositivos para mapear nombres
        var employees = await _employeeRepository.GetAllAsync(cancellationToken);
        var devices = await _deviceRepository.GetAllDevicesAsync(cancellationToken);

        var empDict = employees.ToDictionary(e => e.Id, e => e.GetFullName());
        var devDict = devices.ToDictionary(d => d.Id, d => d.Name);

        // Revisiones y notas de ajustes de RH (concurrencia y auditoría)
        Dictionary<(EmployeeId, DateTime), int>? revisions = null;
        Dictionary<(EmployeeId, DateTime), string?>? adjustmentNotes = null;
        if (_adjustmentRepository != null)
        {
            var adjustments = await _adjustmentRepository.GetByDateRangeAsync(
                request.Date.Date.AddDays(-1),
                request.Date.Date.AddDays(1),
                null,
                empId,
                cancellationToken);
            revisions = adjustments
                .GroupBy(a => (a.EmployeeId, a.Date.Date))
                .ToDictionary(g => g.Key, g => g.Max(a => a.Revision));
            adjustmentNotes = adjustments
                .GroupBy(a => (a.EmployeeId, a.Date.Date))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.UpdatedAt).FirstOrDefault()?.TemporaryExitNote);
        }

        // 4. Mapear tipos de entrada e información de turno por ID de registro
        var assignmentMap = new Dictionary<AttendanceRecordId, (string Type, DateTime Date, bool IsSplit)>();
        var tempExitMap = new Dictionary<AttendanceRecordId, (TemporaryExitStatus Status, int Minutes, DateTime ReturnTime, bool IsAutoLunch, DateTime WorkDate, string? Note, int? Revision)>();
        var returnExitMap = new Dictionary<(EmployeeId, DateTime), (TemporaryExitStatus Status, int Minutes, DateTime ReturnTime, bool IsAutoLunch, DateTime WorkDate, string? Note, int? Revision)>();

        foreach (var da in processed)
        {
            var isSplit = da.ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Partido;
            if (isSplit)
            {
                if (da.CheckInRecordId != null) assignmentMap[da.CheckInRecordId] = ("Entrada B1", da.Date, true);
                if (da.Block1CheckOutRecordId != null) assignmentMap[da.Block1CheckOutRecordId] = ("Salida B1", da.Date, true);
                if (da.Block2CheckInRecordId != null) assignmentMap[da.Block2CheckInRecordId] = ("Entrada B2", da.Date, true);
                if (da.CheckOutRecordId != null) assignmentMap[da.CheckOutRecordId] = ("Salida B2", da.Date, true);
            }
            else
            {
                if (da.CheckInRecordId != null) assignmentMap[da.CheckInRecordId] = ("Entrada", da.Date, false);
                if (da.CheckOutRecordId != null) assignmentMap[da.CheckOutRecordId] = ("Salida", da.Date, false);
                if (da.Block1CheckOutRecordId != null) assignmentMap[da.Block1CheckOutRecordId] = ("Salida B1", da.Date, false);
                if (da.Block2CheckInRecordId != null) assignmentMap[da.Block2CheckInRecordId] = ("Entrada B2", da.Date, false);
            }

            // Caso 11: Salidas temporales y comidas detectadas en la jornada
            var dayRev = revisions != null && revisions.TryGetValue((da.EmployeeId, da.Date.Date), out var rRev) ? rRev : (int?)null;
            var note = da.TemporaryExitNote ?? (adjustmentNotes != null && adjustmentNotes.TryGetValue((da.EmployeeId, da.Date.Date), out var aNote) ? aNote : null);

            foreach (var exit in da.TemporaryExits)
            {
                var exitRecId = AttendanceRecordId.From(exit.ExitRecordId);
                tempExitMap[exitRecId] = (exit.Status, exit.Minutes, exit.ReturnTime, exit.IsAutomaticLunch, da.Date, note, dayRev);
                returnExitMap[(da.EmployeeId, exit.ReturnTime)] = (exit.Status, exit.Minutes, exit.ReturnTime, exit.IsAutomaticLunch, da.Date, note, dayRev);
            }
        }

        var dailyByEmp = processed
            .Where(da => da.Date.Date == request.Date.Date)
            .GroupBy(da => da.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        IReadOnlyDictionary<ShiftId, AttendanceSystem.Domain.Aggregates.ShiftAggregate.Shift>? shiftDict = null;
        if (_shiftRepository != null)
        {
            var allShifts = await _shiftRepository.GetAllAsync(cancellationToken);
            shiftDict = allShifts.ToDictionary(s => s.Id);
        }

        var empAssignments = new Dictionary<EmployeeId, bool>();
        foreach (var emp in employees)
        {
            var assignment = emp.AssignmentOn(request.Date.Date);
            if (assignment.ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Partido)
            {
                empAssignments[emp.Id] = true;
            }
            else if (assignment.ScheduleId != null && shiftDict != null && shiftDict.TryGetValue(assignment.ScheduleId, out var s))
            {
                empAssignments[emp.Id] = s.ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Partido;
            }
            else
            {
                empAssignments[emp.Id] = false;
            }
        }

        // 5. Mapear a DTO
        var dtos = rawRecords.Select(r =>
        {
            string entryType = "No Válida";
            DateTime? assignedDate = null;
            bool isSplit = false;
            bool isTemporaryExit = false;
            bool isTemporaryExitReturn = false;
            TemporaryExitStatus? tempExitStatus = null;
            int? tempExitMinutes = null;
            DateTime? tempExitReturnTime = null;
            bool isAutoLunch = false;
            string? exitAuditNote = null;
            int? dayRevision = null;

            if (assignmentMap.TryGetValue(r.Id, out var assignment))
            {
                entryType = assignment.Type;
                assignedDate = assignment.Date;
                isSplit = assignment.IsSplit;
                dayRevision = revisions != null && revisions.TryGetValue((r.EmployeeId, assignment.Date.Date), out var aRev) ? aRev : null;
            }
            else if (tempExitMap.TryGetValue(r.Id, out var exitInfo))
            {
                isTemporaryExit = true;
                tempExitStatus = exitInfo.Status;
                tempExitMinutes = exitInfo.Minutes;
                tempExitReturnTime = exitInfo.ReturnTime;
                isAutoLunch = exitInfo.IsAutoLunch;
                assignedDate = exitInfo.WorkDate;
                exitAuditNote = exitInfo.Note;
                dayRevision = exitInfo.Revision;

                entryType = exitInfo.Status switch
                {
                    TemporaryExitStatus.Lunch => $"Comida ({exitInfo.Minutes} min)",
                    TemporaryExitStatus.ApprovedPaid => $"Salida con goce ({exitInfo.Minutes} min)",
                    TemporaryExitStatus.ApprovedUnpaid => $"Salida sin goce ({exitInfo.Minutes} min)",
                    TemporaryExitStatus.Dismissed => $"Salida error ({exitInfo.Minutes} min)",
                    _ => $"Salida temporal ({exitInfo.Minutes} min)"
                };
            }
            else if (returnExitMap.TryGetValue((r.EmployeeId, r.GetCivilTime()), out var returnInfo))
            {
                isTemporaryExitReturn = true;
                tempExitStatus = returnInfo.Status;
                tempExitMinutes = returnInfo.Minutes;
                tempExitReturnTime = returnInfo.ReturnTime;
                isAutoLunch = returnInfo.IsAutoLunch;
                assignedDate = returnInfo.WorkDate;
                exitAuditNote = returnInfo.Note;
                dayRevision = returnInfo.Revision;

                entryType = returnInfo.Status == TemporaryExitStatus.Lunch ? "Regreso de comida" : "Regreso de salida";
            }
            else if (dailyByEmp.TryGetValue(r.EmployeeId, out var empDaily))
            {
                isSplit = empDaily.ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Partido;
                dayRevision = revisions != null && revisions.TryGetValue((r.EmployeeId, empDaily.Date.Date), out var dRev) ? dRev : null;
            }
            else if (empAssignments.TryGetValue(r.EmployeeId, out var empIsSplit))
            {
                isSplit = empIsSplit;
            }

            if (!isSplit)
            {
                if (dailyByEmp.TryGetValue(r.EmployeeId, out var empDaily))
                {
                    isSplit = empDaily.ShiftType == AttendanceSystem.Domain.Enumerations.ShiftType.Partido;
                }
                else if (empAssignments.TryGetValue(r.EmployeeId, out var empIsSplit))
                {
                    isSplit = empIsSplit;
                }
            }

            string empName = empDict.TryGetValue(r.EmployeeId, out var name) ? name : r.EmployeeId.Value;
            string devName = devDict.TryGetValue(r.DeviceId, out var dName) ? dName : r.DeviceId.Value;

            return new AttendanceLogViewDto
            {
                Id = r.Id.Value,
                EmployeeId = r.EmployeeId.Value,
                EmployeeName = empName,
                CheckTime = r.GetCivilTime(), // hora civil del marcaje (CheckTime es el instante UTC)
                EntryType = entryType,
                VerifyMethod = r.VerifyMethod.Name,
                DeviceName = devName,
                Status = r.Status.ToString(),
                AssignedDate = r.IsVoided ? null : assignedDate,
                IsSplitShift = isSplit,
                IsTemporaryExit = isTemporaryExit,
                IsTemporaryExitReturn = isTemporaryExitReturn,
                TemporaryExitStatus = tempExitStatus,
                TemporaryExitMinutes = tempExitMinutes,
                TemporaryExitReturnTime = tempExitReturnTime,
                IsAutomaticLunch = isAutoLunch,
                TemporaryExitNote = exitAuditNote,
                DayRevision = dayRevision,
                IsVoided = r.IsVoided,
                VoidedBy = r.VoidedBy,
                VoidReason = r.VoidReason,
                WasCorrected = r.ReplacedByRecordId != null,
                IsCorrection = r.ReplacesRecordId != null,
                IsManual = r.IsManual,
                RegisteredBy = r.RegisteredBy,
                ManualReason = r.Reason
            };
        });

        return dtos.OrderBy(x =>
        {
            if (long.TryParse(x.EmployeeId, out var id)) return id;
            return long.MaxValue;
        }).ThenBy(x => x.EmployeeId)
          .ThenBy(x => x.CheckTime);
    }
}
