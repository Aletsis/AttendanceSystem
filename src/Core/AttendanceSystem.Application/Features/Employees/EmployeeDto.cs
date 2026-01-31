

namespace AttendanceSystem.Application.Features.Employees;

using AttendanceSystem.Domain.Enumerations;

public sealed record EmployeeDto
{
    public string Id { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public DateTime HireDate { get; init; }
    public EmployeeStatus Status { get; init; }
    public Guid BranchId { get; init; }
    public string BranchName { get; init; } = string.Empty;
    public Guid DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public Guid PositionId { get; init; }
    public string PositionName { get; init; } = string.Empty;
    public ShiftType? ShiftType { get; init; }
    public Guid? ScheduleId { get; init; }
    public string? ScheduleName { get; init; }
    public int? RestDay { get; init; } // 0=Domingo, 1=Lunes, etc.
    public string? RestDayName { get; init; }
    public bool OvertimeAuthorized { get; init; }
    public Gender Gender { get; init; }
    public OvertimeCalculationMethod OvertimeCalculationMethod { get; init; }
    public OvertimeCapType OvertimeCapType { get; init; }
    public double? OvertimeCapMinutes { get; init; }
    
    // Biometrics info for display
    public string? CardNumber { get; init; }
    public string? DevicePassword { get; init; }
    public int FingerprintCount { get; init; }
    public bool HasFace { get; init; }
}
