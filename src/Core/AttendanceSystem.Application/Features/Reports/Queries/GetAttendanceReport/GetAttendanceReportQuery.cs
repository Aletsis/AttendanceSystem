using AttendanceSystem.Domain.Services;
using AttendanceSystem.Application.Abstractions;
using MediatR;
using AttendanceSystem.Application.DTOs;
using System;
using System.Collections.Generic;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Reports.Queries.GetAttendanceReport;

public record GetAttendanceReportQuery(
    DateTime StartDate,
    DateTime EndDate,
    BranchId? BranchId = null,
    string? EmployeeId = null) : IRequest<IEnumerable<AttendanceReportViewDto>>, IDateRangeRequest
{
    int IDateRangeRequest.MaxRangeDays => DateRangePolicy.MaxQueryDays;
}
