using AttendanceSystem.Domain.Primitives;

namespace AttendanceSystem.Domain.Aggregates.DeviceAggregate;

public sealed class AdmsIngestionIncident : AggregateRoot<Guid>
{
    public string SerialNumber { get; private set; } = null!;
    public string RawData { get; private set; } = null!;
    public string Error { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public bool Resolved { get; private set; }

    private AdmsIngestionIncident() { }

    public static AdmsIngestionIncident Create(string serialNumber, string rawData, string error)
    {
        return new AdmsIngestionIncident
        {
            Id = Guid.NewGuid(),
            SerialNumber = serialNumber?.Trim().ToUpperInvariant() ?? "UNKNOWN",
            RawData = rawData ?? string.Empty,
            Error = error ?? "Error desconocido de ingesta",
            CreatedAtUtc = DateTime.UtcNow,
            Resolved = false
        };
    }

    public void MarkAsResolved()
    {
        Resolved = true;
    }
}
