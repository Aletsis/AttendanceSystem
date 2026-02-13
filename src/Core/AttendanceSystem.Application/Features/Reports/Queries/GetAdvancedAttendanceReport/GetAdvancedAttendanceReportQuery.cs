using MediatR;
using AttendanceSystem.Application.DTOs;
using System;
using System.Collections.Generic;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Reports.Queries.GetAdvancedAttendanceReport;

public record GetAdvancedAttendanceReportQuery(
    DateTime StartDate, 
    DateTime EndDate, 
    string ReportType, 
    BranchId? BranchId = null, 
    EmployeeId? EmployeeId = null,
    DepartmentId? DepartmentId = null) : IRequest<IEnumerable<AdvancedReportSummaryDto>>;
