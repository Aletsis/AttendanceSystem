namespace AttendanceSystem.Domain.Aggregates.EmployeeAggregate;

using AttendanceSystem.Domain.Primitives;
using AttendanceSystem.Domain.ValueObjects;

public sealed class EmployeeAuditLog : Entity<Guid>
{
    public EmployeeId EmployeeId { get; private set; } = null!;
    public string Action { get; private set; } = string.Empty;
    public DateTime Timestamp { get; private set; }
    public string? UserId { get; private set; }
    public string? UserName { get; private set; }
    public string? Details { get; private set; }
    public string? ChangesJson { get; private set; }

    private EmployeeAuditLog() { }

    public static EmployeeAuditLog Create(
        EmployeeId employeeId,
        string action,
        DateTime timestamp,
        string? userId,
        string? userName,
        string? details,
        string? changesJson = null)
    {
        return new EmployeeAuditLog
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            Action = action,
            Timestamp = timestamp,
            UserId = userId,
            UserName = string.IsNullOrWhiteSpace(userName) ? "Sistema" : userName,
            Details = details,
            ChangesJson = changesJson
        };
    }
}

public sealed class EmployeeAuditFieldChange
{
    public string PropertyName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
