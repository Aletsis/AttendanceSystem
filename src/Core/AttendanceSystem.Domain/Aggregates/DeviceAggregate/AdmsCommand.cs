using AttendanceSystem.Domain.Primitives;

namespace AttendanceSystem.Domain.Aggregates.DeviceAggregate;

public sealed class AdmsCommand : AggregateRoot<Guid>
{
    public string SerialNumber { get; private set; } = null!;
    public string CommandId { get; private set; } = null!;
    public string CommandText { get; private set; } = null!;
    public AdmsCommandStatus Status { get; private set; }
    public bool IsDestructive { get; private set; }
    public Guid? DownloadLogId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public int? ReturnCode { get; private set; }
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }

    private AdmsCommand() { }

    public static AdmsCommand Create(
        string serialNumber,
        string commandId,
        string commandText,
        Guid? downloadLogId = null)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
            throw new ArgumentException("El número de serie es requerido", nameof(serialNumber));
        if (string.IsNullOrWhiteSpace(commandId))
            throw new ArgumentException("El CommandId es requerido", nameof(commandId));
        if (string.IsNullOrWhiteSpace(commandText))
            throw new ArgumentException("El texto del comando es requerido", nameof(commandText));

        bool isDestructive = IsDestructiveCommand(commandText);

        return new AdmsCommand
        {
            Id = Guid.NewGuid(),
            SerialNumber = serialNumber.Trim().ToUpperInvariant(),
            CommandId = commandId.Trim(),
            CommandText = commandText.Trim(),
            Status = AdmsCommandStatus.Pending,
            IsDestructive = isDestructive,
            DownloadLogId = downloadLogId,
            CreatedAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };
    }

    public void MarkAsSent()
    {
        Status = AdmsCommandStatus.Sent;
        SentAtUtc = DateTime.UtcNow;
    }

    public void MarkAsConfirmed(int returnCode)
    {
        Status = AdmsCommandStatus.Confirmed;
        ConfirmedAtUtc = DateTime.UtcNow;
        ReturnCode = returnCode;
        ErrorMessage = null;
    }

    public void MarkAsFailed(int? returnCode, string? errorMessage)
    {
        Status = AdmsCommandStatus.Failed;
        ConfirmedAtUtc = DateTime.UtcNow;
        ReturnCode = returnCode;
        ErrorMessage = errorMessage;
        RetryCount++;
    }

    public void MarkAsExpired()
    {
        Status = AdmsCommandStatus.Expired;
        ErrorMessage = "Expirado por falta de acuse del terminal (TTL excedido)";
    }

    public bool CanRetryAutomatically()
    {
        // Solo reintentar automáticamente comandos seguros/idempotentes
        return !IsDestructive && Status == AdmsCommandStatus.Failed && RetryCount < 3;
    }

    public void ResetForRetry()
    {
        if (IsDestructive)
            throw new InvalidOperationException("Comandos destructivos con resultado incierto requieren reconciliación manual y no pueden reintentarse automáticamente.");

        Status = AdmsCommandStatus.Pending;
        SentAtUtc = null;
        ConfirmedAtUtc = null;
    }

    private static bool IsDestructiveCommand(string command)
    {
        var upper = command.ToUpperInvariant();
        return upper.Contains("CLEAR ATTLOG") ||
               upper.Contains("CLEAR LOG") ||
               upper.Contains("CLEAR DATA") ||
               upper.Contains("RESET") ||
               upper.Contains("REBOOT") ||
               upper.Contains("DELETE");
    }
}
