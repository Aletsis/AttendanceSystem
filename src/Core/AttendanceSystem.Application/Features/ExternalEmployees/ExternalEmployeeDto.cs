namespace AttendanceSystem.Application.Features.ExternalEmployees;

using AttendanceSystem.Domain.Enumerations;

public sealed record ExternalEmployeeDto
{
    public Guid Id { get; init; }
    public Guid BranchId { get; init; }
    public string BranchCode { get; init; } = string.Empty;
    public string BranchName { get; init; } = string.Empty;
    public string EmployeeNumber { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Position { get; init; }
    public string? Department { get; init; }
    public EmployeeStatus Status { get; init; }
    public string? CardNumber { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
