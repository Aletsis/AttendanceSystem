using System.Globalization;
using System.Net;

namespace ZKTecoRealtimeProbe;

internal sealed record DeviceEndpoint(string IpAddress, int Port)
{
    public override string ToString() => $"{IpAddress}:{Port}";
}

internal sealed class ProbeOptions
{
    public List<DeviceEndpoint> Devices { get; } = new();
    public int? CommKey { get; private set; }
    public int MachineNumber { get; private set; } = 1;
    public int HeartbeatSeconds { get; private set; } = 30;
    public int HeartbeatFailuresToDisconnect { get; private set; } = 2;
    public int EventMask { get; private set; } = 65535;
    public bool CatchUpAfterReconnect { get; private set; } = true;
    public int ReadWindowMinutes { get; private set; } = 60;
    public bool Verbose { get; private set; }
    public string LogDirectory { get; private set; } = Path.Combine(AppContext.BaseDirectory, "probe-logs");

    public const string Usage = """
        Uso: ZKTecoRealtimeProbe --device <ip[:puerto]> [--device <ip[:puerto]> ...] [opciones]

          --device <ip[:puerto]>     Reloj a vigilar (puerto 4370 por omisión). Repetible: un hilo STA por reloj.
          --comm-key <n>             Contraseña de comunicación (Comm Key) del reloj, si tiene.
          --machine <n>              Número de máquina del SDK (1 por omisión).
          --heartbeat <seg>          Intervalo del latido GetDeviceTime (30 por omisión).
          --heartbeat-failures <n>   Latidos fallidos seguidos para dar la conexión por caída (2 por omisión).
          --event-mask <n>           Máscara de RegEvent (65535 = todos; 1 = solo checadas).
          --no-catch-up              No leer el hueco con ReadTimeGLogData al reconectar.
          --window <min>             Ventana en minutos del comando 't' (60 por omisión).
          --verbose                  Registrar cada latido.
          --log-dir <ruta>           Carpeta de bitácoras (probe-logs junto al ejecutable por omisión).
        """;

    public static ProbeOptions? Parse(string[] args, out string? error)
    {
        var options = new ProbeOptions();
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next()
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"Falta el valor de {arg}.");
                return args[++i];
            }
            int NextInt(int min)
            {
                var raw = Next();
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min)
                    throw new ArgumentException($"Valor inválido para {arg}: '{raw}'.");
                return value;
            }

            try
            {
                switch (arg)
                {
                    case "--device": options.Devices.Add(ParseEndpoint(Next())); break;
                    case "--comm-key": options.CommKey = NextInt(0); break;
                    case "--machine": options.MachineNumber = NextInt(1); break;
                    case "--heartbeat": options.HeartbeatSeconds = NextInt(5); break;
                    case "--heartbeat-failures": options.HeartbeatFailuresToDisconnect = NextInt(1); break;
                    case "--event-mask": options.EventMask = NextInt(1); break;
                    case "--no-catch-up": options.CatchUpAfterReconnect = false; break;
                    case "--window": options.ReadWindowMinutes = NextInt(1); break;
                    case "--verbose": options.Verbose = true; break;
                    case "--log-dir": options.LogDirectory = Next(); break;
                    case "-h" or "--help": return null;
                    default: throw new ArgumentException($"Opción desconocida: {arg}");
                }
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        if (options.Devices.Count == 0)
        {
            error = "Indique al menos un --device.";
            return null;
        }

        return options;
    }

    private static DeviceEndpoint ParseEndpoint(string raw)
    {
        var parts = raw.Split(':');
        if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out _))
            throw new ArgumentException($"Dirección inválida: '{raw}'.");

        var port = 4370;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out port) || port is < 1 or > 65535))
            throw new ArgumentException($"Puerto inválido: '{raw}'.");

        return new DeviceEndpoint(parts[0], port);
    }
}
