namespace AttendanceSystem.Application.DTOs;

public sealed record EmployeeAuditLogDto
{
    public Guid Id { get; init; }
    public string EmployeeId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public string? UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string? Details { get; init; }
    public List<EmployeeAuditFieldChangeDto> Changes { get; init; } = new();
}

public sealed record EmployeeAuditFieldChangeDto
{
    public string PropertyName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
}
