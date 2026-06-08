using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;
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

    public GetAttendanceLogsQueryHandler(
        IAttendanceRepository attendanceRepository,
        IDailyAttendanceRepository dailyAttendanceRepository,
        IEmployeeRepository employeeRepository,
        IDeviceRepository deviceRepository)
    {
        _attendanceRepository = attendanceRepository;
        _dailyAttendanceRepository = dailyAttendanceRepository;
        _employeeRepository = employeeRepository;
        _deviceRepository = deviceRepository;
    }

    public async Task<IEnumerable<AttendanceLogViewDto>> Handle(GetAttendanceLogsQuery request, CancellationToken cancellationToken)
    {
        var dateOnly = DateOnly.FromDateTime(request.Date);
        EmployeeId? empId = !string.IsNullOrEmpty(request.EmployeeId) ? EmployeeId.From(request.EmployeeId) : null;

        // 1. Fetch Raw Records
        var rawRecords = await _attendanceRepository.GetByDateRangeAsync(
            dateOnly,
            dateOnly,
            empId,
            cancellationToken);

        // 2. Fetch Processed Attendance (to check assignments)
        // We extend the range to Date-1 and Date+1 to catch cross-day assignments 
        // (e.g. Monday's exit occurring on Tuesday morning).
        var processed = await _dailyAttendanceRepository.GetByDateRangeAsync(
            request.Date.Date.AddDays(-1),
            request.Date.Date.AddDays(1),
            null, // Branch
            empId,
            cancellationToken);

        // 3. Metadata Lookups
        var employees = await _employeeRepository.GetAllAsync(cancellationToken);
        var devices = await _deviceRepository.GetAllDevicesAsync(cancellationToken);

        var empDict = employees.ToDictionary(e => e.Id, e => e.GetFullName());
        var devDict = devices.ToDictionary(d => d.Id, d => d.Name);

        // 4. Map Entry Types by Record ID
        var assignmentMap = new Dictionary<AttendanceRecordId, (string Type, DateTime Date)>();
        foreach (var da in processed)
        {
            if (da.CheckInRecordId != null) assignmentMap[da.CheckInRecordId] = ("Entrada", da.Date);
            if (da.CheckOutRecordId != null) assignmentMap[da.CheckOutRecordId] = ("Salida", da.Date);
        }

        // 5. Map to DTO
        var dtos = rawRecords.Select(r => 
        {
            string entryType = "No Válida";
            DateTime? assignedDate = null;

            if (assignmentMap.TryGetValue(r.Id, out var assignment))
            {
                entryType = assignment.Type;
                assignedDate = assignment.Date;
            }

            string empName = empDict.TryGetValue(r.EmployeeId, out var name) ? name : r.EmployeeId.Value;
            string devName = devDict.TryGetValue(r.DeviceId, out var dName) ? dName : r.DeviceId.Value;

            return new AttendanceLogViewDto
            {
                Id = r.Id.Value,
                EmployeeId = r.EmployeeId.Value,
                EmployeeName = empName,
                CheckTime = r.CheckTime,
                EntryType = entryType,
                VerifyMethod = r.VerifyMethod.Name,
                DeviceName = devName,
                Status = r.Status.ToString(),
                AssignedDate = assignedDate
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
