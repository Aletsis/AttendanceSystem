using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Primitives;

namespace AttendanceSystem.Domain.Aggregates.ExternalLogAggregate;

public sealed class ExternalAttendanceLog : AggregateRoot<Guid>
{
    public string BranchCode { get; private set; } = null!;
    public string EmployeeId { get; private set; } = null!;
    public DateTime CheckTime { get; private set; }
    public int VerifyMethod { get; private set; }
    public int CheckType { get; private set; }
    public string? SourceDevice { get; private set; }
    public ExternalLogStatus Status { get; private set; } = null!;
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? TransferredAt { get; private set; }

    private ExternalAttendanceLog() { }

    public static ExternalAttendanceLog Create(
        string branchCode,
        string employeeId,
        DateTime checkTime,
        int verifyMethod,
        int checkType,
        string? sourceDevice = null)
    {
        if (string.IsNullOrWhiteSpace(branchCode))
            throw new DomainException("El código de sucursal es requerido.");
        if (string.IsNullOrWhiteSpace(employeeId))
            throw new DomainException("El ID del empleado es requerido.");

        return new ExternalAttendanceLog
        {
            Id = Guid.NewGuid(),
            BranchCode = branchCode.ToUpper(),
            EmployeeId = employeeId,
            CheckTime = checkTime,
            VerifyMethod = verifyMethod,
            CheckType = checkType,
            SourceDevice = sourceDevice,
            Status = ExternalLogStatus.Pending,
            RetryCount = 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void MarkAsTransferred()
    {
        Status = ExternalLogStatus.Transferred;
        TransferredAt = DateTime.UtcNow;
        ErrorMessage = null;
    }

    public void MarkAsFailed(string error)
    {
        Status = ExternalLogStatus.Failed;
        ErrorMessage = error;
        RetryCount++;
    }

    public void ResetForRetry()
    {
        Status = ExternalLogStatus.Pending;
    }
}
