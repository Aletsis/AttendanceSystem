namespace AttendanceSystem.ZKTeco.Realtime;

/// <summary>Reloj que se escucha en tiempo real. <see cref="DeviceId"/> es el identificador del reloj en el sistema.</summary>
public sealed record RealtimeDeviceTarget(string DeviceId, string IpAddress, int Port)
{
    public string IpKey => $"{IpAddress}:{Port}";
}

/// <summary>Marcaje recibido en tiempo real. <see cref="CheckTime"/> es la hora civil que marca el reloj, igual que en la descarga.</summary>
public sealed record RealtimePunch(string UserId, DateTime CheckTime, int VerifyMethod, int InOutMode, int WorkCode, bool IsInvalid = false);

/// <summary>
/// Estado de la conexión de escucha. Al conectarse, <see cref="LastAliveUtc"/> es la última vez que la conexión anterior
/// respondió (null si es la primera): lo marcado desde entonces hay que recuperarlo con una descarga.
/// </summary>
public sealed record RealtimeConnectionStatus(bool Connected, DateTime? LastAliveUtc, string Message);

/// <summary>Evento hacia el suscriptor: un marcaje o un cambio de conexión de un reloj.</summary>
public sealed record RealtimeEvent(
    string DeviceId,
    DateTime OccurredAtUtc,
    RealtimePunch? Punch = null,
    RealtimeConnectionStatus? Connection = null);

public sealed class ZKTecoRealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>Cada cuánto se lee la hora del reloj para saber si la conexión sigue viva (el SDK no avisa la caída).</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Esperas entre reintentos de conexión; el último se repite.</summary>
    public TimeSpan[] ReconnectDelays { get; set; } =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    /// <summary>Eventos que se guardan mientras no hay suscriptor; al llenarse se descartan los más viejos.</summary>
    public int ChannelCapacity { get; set; } = 10_000;
}
