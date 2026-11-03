using System.Globalization;

namespace ZKTecoRealtimeProbe;

/// <summary>
/// Bitácora de consola + archivo de texto + CSV de checadas, segura entre los hilos de los relojes.
/// </summary>
internal sealed class ProbeLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _text;
    private readonly StreamWriter _csv;

    public string TextPath { get; }
    public string CsvPath { get; }

    public ProbeLog(string directory)
    {
        Directory.CreateDirectory(directory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        TextPath = Path.Combine(directory, $"probe-{stamp}.log");
        CsvPath = Path.Combine(directory, $"probe-punches-{stamp}.csv");

        _text = new StreamWriter(TextPath, append: false) { AutoFlush = true };
        _csv = new StreamWriter(CsvPath, append: false) { AutoFlush = true };
        _csv.WriteLine("recibido_pc,reloj,origen,evento,usuario,hora_checada,invalida,estado,verificacion,workcode,atraso_seg,desfase_reloj_seg,latencia_estimada_seg");
    }

    public void Info(string device, string message) => Write(device, "INF", message, ConsoleColor.Gray);
    public void Success(string device, string message) => Write(device, "OK ", message, ConsoleColor.Green);
    public void Warn(string device, string message) => Write(device, "WRN", message, ConsoleColor.Yellow);
    public void Error(string device, string message) => Write(device, "ERR", message, ConsoleColor.Red);
    public void Event(string device, string message) => Write(device, "EVT", message, ConsoleColor.DarkGray);
    public void Punch(string device, string message) => Write(device, "CHK", message, ConsoleColor.Cyan);

    public void PunchRow(
        DateTime receivedAt, string device, string source, string eventName, string userId, DateTime punchTime,
        int isInvalid, int state, int verifyMethod, string workCode, TimeSpan? clockDrift)
    {
        var lag = receivedAt - punchTime;
        // El desfase es hora del reloj − hora del PC; la checada viene en hora del reloj.
        var latency = clockDrift is { } drift ? lag + drift : (TimeSpan?)null;

        var row = string.Join(",",
            receivedAt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            device, source, eventName, Csv(userId),
            punchTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            isInvalid, state, verifyMethod, Csv(workCode),
            Seconds(lag), clockDrift is { } d ? Seconds(d) : "", latency is { } l ? Seconds(l) : "");

        lock (_gate) _csv.WriteLine(row);
    }

    public void Raw(string line)
    {
        lock (_gate)
        {
            Console.WriteLine(line);
            _text.WriteLine(line);
        }
    }

    private void Write(string device, string level, string message, ConsoleColor color)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] [{device}] {message}";
        lock (_gate)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(line);
            Console.ForegroundColor = previous;
            _text.WriteLine(line);
        }
    }

    private static string Seconds(TimeSpan span) => span.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Csv(string value) => value.Contains(',') || value.Contains('"')
        ? $"\"{value.Replace("\"", "\"\"")}\""
        : value;

    public void Dispose()
    {
        lock (_gate)
        {
            _text.Dispose();
            _csv.Dispose();
        }
    }
}
