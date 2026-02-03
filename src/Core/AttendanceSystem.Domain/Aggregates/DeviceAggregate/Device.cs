namespace AttendanceSystem.Domain.Aggregates.DeviceAggregate;

public class Device : AggregateRoot<DeviceId>
{
    public string Name { get; private set; }
    public string IpAddress { get; private set; }
    public int Port { get; private set; }
    public string? Location { get; private set; }
    public bool IsActive { get; private set; }
    public bool ShouldClearAfterDownload { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastDownloadAt { get; private set; }
    public int TotalDownloadCount { get; private set; }
    public DeviceStatus Status { get; private set; }
    public DeviceDownloadMethod DownloadMethod { get; private set; }
    public DeviceHardwareInfo HardwareInfo { get; private set; }


    private Device() { } // Para EF Core

    public static Device Create(
        string deviceId,
        string name,
        string ipAddress,
        int port,
        string? location = null,
        bool shouldClearAfterDownload = false,
        DeviceDownloadMethod downloadMethod = DeviceDownloadMethod.Sdk,
        string? serialNumber = null)
    {
        // Validaciones
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del dispositivo es requerido");

        // Si es SDK, validar IP. Si es ADMS, podría no ser requerida la IP si el dispositivo empuja, 
        // pero generalmente la IP se usa para identificación o gestión remota de todas formas.
        // Asumiremos que se mantiene la validación de IP por ahora para mantener consistencia 
        // o si el usuario quiere que ADMS no requiera IP, debería especificarlo.
        // El código original valida IP.

        // CORRECTION: ADMS might not have a static IP or we might not know it initially if dynamic.
        // However, for now we keep validating IP but maybe we loosen check if ADMS?
        // Let's keep it as is for now.

        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new DomainException("La dirección IP es requerida");

        if (!IsValidIpAddress(ipAddress))
            throw new DomainException($"Dirección IP inválida: {ipAddress}");

        if (port < 1 || port > 65535)
            throw new DomainException($"Puerto inválido: {port}");

        var device = new Device
        {
            Id = DeviceId.From(deviceId),
            Name = name,
            IpAddress = ipAddress,
            Port = port,
            Location = location,
            IsActive = true,
            ShouldClearAfterDownload = shouldClearAfterDownload,
            DownloadMethod = downloadMethod,
            CreatedAt = DateTime.UtcNow,
            Status = DeviceStatus.Disconnected,
            TotalDownloadCount = 0,
            HardwareInfo = DeviceHardwareInfo.Empty
        };

        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            device.HardwareInfo = device.HardwareInfo with { SerialNumber = serialNumber };
        }

        device.AddDomainEvent(new DeviceRegisteredEvent(
            device.Id,
            device.Name,
            device.IpAddress));

        return device;
    }

    public void UpdateConfiguration(
        string name,
        string ipAddress,
        int port,
        string? location,
        bool shouldClearAfterDownload,
        DeviceDownloadMethod downloadMethod,
        string? serialNumber = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del dispositivo es requerido");

        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new DomainException("La dirección IP es requerida");

        if (!IsValidIpAddress(ipAddress))
            throw new DomainException($"Dirección IP inválida: {ipAddress}");

        Name = name;
        IpAddress = ipAddress;
        Port = port;
        Location = location;
        ShouldClearAfterDownload = shouldClearAfterDownload;
        DownloadMethod = downloadMethod;

        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
             HardwareInfo = HardwareInfo with { SerialNumber = serialNumber };
        }

        AddDomainEvent(new DeviceConfigurationUpdatedEvent(Id));
    }

    public void UpdateDeviceInfo(DeviceHardwareInfo info)
    {
        HardwareInfo = info;
    }

    public void Activate()
    {
        if (IsActive)
            throw new DomainException("El dispositivo ya está activo");

        IsActive = true;
        AddDomainEvent(new DeviceActivatedEvent(Id));
    }

    public void Deactivate()
    {
        if (!IsActive)
            throw new DomainException("El dispositivo ya está inactivo");

        IsActive = false;
        Status = DeviceStatus.Disconnected;
        AddDomainEvent(new DeviceDeactivatedEvent(Id));
    }

    public void RecordSuccessfulDownload(int recordCount)
    {
        LastDownloadAt = DateTime.UtcNow;
        TotalDownloadCount++;
        Status = DeviceStatus.Online;

        AddDomainEvent(new DeviceDownloadCompletedEvent(
            Id,
            recordCount,
            LastDownloadAt.Value));
    }

    public void RecordFailedDownload(string errorMessage)
    {
        Status = DeviceStatus.Error;

        AddDomainEvent(new DeviceDownloadFailedEvent(
            Id,
            errorMessage,
            DateTime.UtcNow));
    }

    public void MarkAsOnline()
    {
        Status = DeviceStatus.Online;
    }

    public void MarkAsOffline()
    {
        Status = DeviceStatus.Offline;
    }

    private static bool IsValidIpAddress(string ipAddress)
    {
        return System.Net.IPAddress.TryParse(ipAddress, out _);
    }
}

// Enumeración para estado del dispositivo
public sealed class DeviceStatus : Enumeration
{
    public static readonly DeviceStatus Online = new(1, "En Línea");
    public static readonly DeviceStatus Offline = new(2, "Desconectado");
    public static readonly DeviceStatus Disconnected = new(3, "Sin Conexión");
    public static readonly DeviceStatus Error = new(4, "Error");

    private DeviceStatus(int id, string name) : base(id, name) { }

    public static DeviceStatus FromValue(int value)
    {
        return value switch
        {
            1 => Online,
            2 => Offline,
            3 => Disconnected,
            4 => Error,
            _ => throw new DomainException($"Estado de dispositivo inválido: {value}")
        };
    }
}

// Eventos de dominio del agregado Device
public sealed record DeviceRegisteredEvent(
    DeviceId DeviceId,
    string Name,
    string IpAddress) : DomainEvent(DateTime.UtcNow);

public sealed record DeviceConfigurationUpdatedEvent(
    DeviceId DeviceId) : DomainEvent(DateTime.UtcNow);

public sealed record DeviceActivatedEvent(
    DeviceId DeviceId) : DomainEvent(DateTime.UtcNow);

public sealed record DeviceDeactivatedEvent(
    DeviceId DeviceId) : DomainEvent(DateTime.UtcNow);

public sealed record DeviceDownloadCompletedEvent(
    DeviceId DeviceId,
    int RecordCount,
    DateTime DownloadedAt) : DomainEvent(DateTime.UtcNow);

public sealed record DeviceDownloadFailedEvent(
    DeviceId DeviceId,
    string ErrorMessage,
    DateTime FailedAt) : DomainEvent(DateTime.UtcNow);