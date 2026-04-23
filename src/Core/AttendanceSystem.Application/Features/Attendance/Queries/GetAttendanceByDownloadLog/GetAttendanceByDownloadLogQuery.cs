using MediatR;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using System.Collections.Generic;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetAttendanceByDownloadLog;

public sealed record GetAttendanceByDownloadLogQuery(DownloadLogId DownloadLogId) 
    : IRequest<IEnumerable<AttendanceLogViewDto>>;
