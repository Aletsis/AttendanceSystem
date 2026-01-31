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
        // Note: DailyAttendanceRepository usually accepts DateTime for range, need to check if it accepts DateOnly or DateTime.
        // Based on previous refactor, it likely takes DateTime.
        var processed = await _dailyAttendanceRepository.GetByDateRangeAsync(
            request.Date.Date,
            request.Date.Date,
            null, // Branch
            empId,
            cancellationToken);

        // 3. Metadata Lookups
        var employees = await _employeeRepository.GetAllAsync(cancellationToken);
        var devices = await _deviceRepository.GetAllDevicesAsync(cancellationToken);

        var empDict = employees.ToDictionary(e => e.Id, e => e.GetFullName());
        var devDict = devices.ToDictionary(d => d.Id, d => d.Name);

        // 4. Map Entry Types by Record ID
        var assignmentMap = new Dictionary<AttendanceRecordId, string>();
        foreach (var da in processed)
        {
            if (da.CheckInRecordId != null) assignmentMap[da.CheckInRecordId] = "Entrada";
            if (da.CheckOutRecordId != null) assignmentMap[da.CheckOutRecordId] = "Salida";
        }

        // 5. Map to DTO
        var dtos = rawRecords.Select(r => 
        {
            string entryType = "No Válida";
            if (assignmentMap.TryGetValue(r.Id, out var assignedType))
            {
                entryType = assignedType;
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
                Status = r.Status.ToString()
            };
        });

        return dtos.OrderByDescending(x => x.CheckTime);
    }
}
