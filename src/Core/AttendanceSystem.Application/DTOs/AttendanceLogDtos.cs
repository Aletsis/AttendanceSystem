using System;

namespace AttendanceSystem.Application.DTOs;

public sealed record AttendanceLogViewDto
{
    public Guid Id { get; init; }
    public string EmployeeId { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public DateTime CheckTime { get; init; }
    public string EntryType { get; init; } = "No Válida"; 
    public string VerifyMethod { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}
