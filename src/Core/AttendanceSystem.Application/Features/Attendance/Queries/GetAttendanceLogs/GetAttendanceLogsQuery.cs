using MediatR;
using AttendanceSystem.Application.DTOs;
using System;
using System.Collections.Generic;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetAttendanceLogs;

public record GetAttendanceLogsQuery(DateTime Date, string? EmployeeId = null) : IRequest<IEnumerable<AttendanceLogViewDto>>;
