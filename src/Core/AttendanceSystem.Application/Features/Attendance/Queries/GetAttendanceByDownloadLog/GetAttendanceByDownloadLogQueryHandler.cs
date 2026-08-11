using MediatR;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.ValueObjects;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetAttendanceByDownloadLog;

public class GetAttendanceByDownloadLogQueryHandler : IRequestHandler<GetAttendanceByDownloadLogQuery, IEnumerable<AttendanceLogViewDto>>
{
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDeviceRepository _deviceRepository;

    public GetAttendanceByDownloadLogQueryHandler(
        IAttendanceRepository attendanceRepository,
        IEmployeeRepository employeeRepository,
        IDeviceRepository deviceRepository)
    {
        _attendanceRepository = attendanceRepository;
        _employeeRepository = employeeRepository;
        _deviceRepository = deviceRepository;
    }

    public async Task<IEnumerable<AttendanceLogViewDto>> Handle(GetAttendanceByDownloadLogQuery request, CancellationToken cancellationToken)
    {
        // 1. Obtener los registros de asistencia asociados al DownloadLogId proporcionado
        var records = await _attendanceRepository.GetByDownloadLogIdAsync(request.DownloadLogId, cancellationToken);

        if (!records.Any())
            return Enumerable.Empty<AttendanceLogViewDto>();

        // 2. Busqueda de empleados y dispositivos para mapear nombres
        var employees = await _employeeRepository.GetAllAsync(cancellationToken);
        var devices = await _deviceRepository.GetAllDevicesAsync(cancellationToken);

        var empDict = employees.ToDictionary(e => e.Id, e => e.GetFullName());
        var devDict = devices.ToDictionary(d => d.Id, d => d.Name);

        // 3. Mapeo a DTO
        var dtos = records.Select(r => 
        {
            string empName = empDict.TryGetValue(r.EmployeeId, out var name) ? name : r.EmployeeId.Value;
            string devName = devDict.TryGetValue(r.DeviceId, out var dName) ? dName : r.DeviceId.Value;

            return new AttendanceLogViewDto
            {
                Id = r.Id.Value,
                EmployeeId = r.EmployeeId.Value,
                EmployeeName = empName,
                CheckTime = r.CheckTime,
                EntryType = "Pendiente", // Since they are new, they might not be processed yet
                VerifyMethod = r.VerifyMethod.Name,
                DeviceName = devName,
                Status = r.Status.ToString()
            };
        });

        return dtos;
    }
}
