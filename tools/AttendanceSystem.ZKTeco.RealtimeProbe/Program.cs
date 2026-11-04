using System.Globalization;
using System.Runtime.InteropServices;

namespace AttendanceSystem.ZKTeco.RealtimeProbe;

/// <summary>
/// Prueba de la descarga en tiempo real por SDK (zkemkeeper) contra un reloj real, antes de diseñar el listener del
/// servicio ZKTeco. Responde: ¿llegan los eventos de marcaje desde un hilo del pool (MTA) o hace falta un hilo STA?,
/// ¿basta con sondear ReadRTLog/GetRTLog o hace falta un ciclo de mensajes?, ¿se pueden intercalar otras operaciones
/// en la misma conexión? y ¿acepta el firmware una segunda conexión mientras la primera escucha?
/// </summary>
public static class Program
{
    private const int Machine = 1;

    private sealed record Options(
        string Mode,
        string Ip,
        int Port,
        int Seconds,
        bool Sta,
        bool Poll,
        int PollMs,
        int OpEverySeconds,
        bool OpCrossThread,
        int SecondAtSeconds,
        int WatchSeconds);

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Options options;
        try
        {
            options = Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintUsage();
            return 2;
        }

        Log($"Modo {options.Mode} contra {options.Ip}:{options.Port} durante {options.Seconds}s " +
            $"(hilo {(options.Sta ? "STA" : "MTA")}, {(options.Poll ? $"sondeo cada {options.PollMs} ms" : "sin sondeo")})");

        var exitCode = 1;
        var body = () => exitCode = options.Mode switch
        {
            "second" => RunSecond(options),
            "watch" => RunWatch(options),
            _ => RunListen(options),
        };
        if (options.Sta)
        {
            var thread = new Thread(() => body()) { IsBackground = false, Name = "zk-sta" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
        else
        {
            // Hilo del pool, como hoy hace ZKTecoDeviceClient con Task.Run.
            Task.Run(body).GetAwaiter().GetResult();
        }
        return exitCode;
    }

    /// <summary>
    /// Escucha marcajes en una conexión y, opcionalmente, intercala una lectura de la hora para simular otras
    /// operaciones sobre la misma conexión (en el hilo dueño o desde otro hilo).
    /// </summary>
    private static int RunListen(Options o)
    {
        var zk = new zkemkeeper.CZKEMClass();
        var gate = new object();
        var stats = new Stats();
        Subscribe(zk, "A", stats);

        if (!Connect(zk, "A", o)) return 1;
        Register(zk, "A");

        var deadline = DateTime.UtcNow.AddSeconds(o.Seconds);
        var nextOp = o.OpEverySeconds > 0 ? DateTime.UtcNow.AddSeconds(o.OpEverySeconds) : DateTime.MaxValue;
        Log("Marque en el reloj ahora. Ctrl+C para salir.");

        while (DateTime.UtcNow < deadline)
        {
            if (o.Poll)
            {
                lock (gate) PollOnce(zk, "A", stats);
            }

            if (DateTime.UtcNow >= nextOp)
            {
                nextOp = DateTime.UtcNow.AddSeconds(o.OpEverySeconds);
                if (o.OpCrossThread)
                {
                    // Otro hilo sobre el mismo objeto COM: si el dueño es STA y no bombea mensajes, esto se bloquea.
                    var op = Task.Run(() => { lock (gate) ReadTime(zk, "A (otro hilo)"); });
                    if (!op.Wait(TimeSpan.FromSeconds(10)))
                    {
                        Log("A (otro hilo): la llamada no terminó en 10 s (bloqueo entre apartamentos)");
                    }
                }
                else
                {
                    lock (gate) ReadTime(zk, "A");
                }
            }

            Wait(o.Sta, o.Poll ? o.PollMs : 50);
        }

        Disconnect(zk, "A");
        stats.Print();
        return 0;
    }

    /// <summary>
    /// A escucha; a los N segundos B intenta conectarse al mismo reloj, lee hora y conteo de marcajes y se desconecta.
    /// Después A sigue escuchando para ver si su conexión sobrevivió.
    /// </summary>
    private static int RunSecond(Options o)
    {
        var a = new zkemkeeper.CZKEMClass();
        var stats = new Stats();
        Subscribe(a, "A", stats);
        if (!Connect(a, "A", o)) return 1;
        Register(a, "A");

        var start = DateTime.UtcNow;
        var deadline = start.AddSeconds(o.Seconds);
        var secondDone = false;
        Log($"A escuchando. B se conectará a los {o.SecondAtSeconds}s. Marque antes y después de eso.");

        while (DateTime.UtcNow < deadline)
        {
            if (o.Poll) PollOnce(a, "A", stats);

            if (!secondDone && DateTime.UtcNow - start >= TimeSpan.FromSeconds(o.SecondAtSeconds))
            {
                secondDone = true;
                var b = new zkemkeeper.CZKEMClass();
                if (Connect(b, "B", o))
                {
                    ReadTime(b, "B");
                    var count = 0;
                    var ok = b.GetDeviceStatus(Machine, 6, ref count);
                    Log($"B: GetDeviceStatus(6 = marcajes) -> {ok}, {count}");
                    ReadTime(a, "A (con B conectado)");
                    Disconnect(b, "B");
                }
                ReadTime(a, "A (después de B)");
                Log("A: marque de nuevo para comprobar que los eventos siguen llegando.");
            }

            Wait(o.Sta, o.Poll ? o.PollMs : 50);
        }

        Disconnect(a, "A");
        stats.Print();
        return 0;
    }

    /// <summary>
    /// Escucha sin sondeo y vigila la conexión leyendo la hora cada N segundos. Si falla (o llega OnDisConnected),
    /// desconecta y reintenta conectar y registrar eventos hasta lograrlo, para medir cuánto tarda en detectarse una
    /// caída (reinicio del reloj, cable desconectado) y si los marcajes vuelven a llegar tras reconectar.
    /// </summary>
    private static int RunWatch(Options o)
    {
        var zk = new zkemkeeper.CZKEMClass();
        var stats = new Stats();
        Subscribe(zk, "A", stats);
        if (!Connect(zk, "A", o)) return 1;
        Register(zk, "A");

        var deadline = DateTime.UtcNow.AddSeconds(o.Seconds);
        var disconnectsSeen = 0;
        Log($"Vigilando cada {o.WatchSeconds}s. Marque, luego reinicie el reloj (o desconecte su cable) y marque al volver.");

        while (DateTime.UtcNow < deadline)
        {
            Wait(false, o.WatchSeconds * 1000);
            var eventFired = stats.Disconnects > disconnectsSeen;
            disconnectsSeen = stats.Disconnects;
            if (!eventFired && ReadTime(zk, "A", quiet: true)) continue;

            var down = DateTime.UtcNow;
            Log($"A: conexión perdida ({(eventFired ? "evento OnDisConnected" : "la lectura de hora falló")}); reconectando...");
            zk.Disconnect();
            var attempts = 0;
            while (DateTime.UtcNow < deadline)
            {
                attempts++;
                if (Connect(zk, $"A (reintento {attempts})", o))
                {
                    Register(zk, "A");
                    Log($"A: reconectado tras {(DateTime.UtcNow - down).TotalSeconds:F0}s y {attempts} intentos. Marque para comprobar.");
                    break;
                }
                Wait(false, 5000);
            }
        }

        Disconnect(zk, "A");
        stats.Print();
        return 0;
    }

    private static void Subscribe(zkemkeeper.CZKEMClass zk, string name, Stats stats)
    {
        zk.OnAttTransactionEx += (enroll, invalid, state, verify, year, month, day, hour, minute, second, workCode) =>
        {
            stats.Events++;
            Log($"{name}: MARCAJE usuario={enroll} hora={year:0000}-{month:00}-{day:00} {hour:00}:{minute:00}:{second:00} " +
                $"estado={state} verificación={verify} inválido={invalid} workcode={workCode}");
        };
        zk.OnVerify += userId => Log($"{name}: verificación usuario={userId}");
        zk.OnConnected += () => Log($"{name}: evento OnConnected");
        zk.OnDisConnected += () =>
        {
            stats.Disconnects++;
            Log($"{name}: evento OnDisConnected");
        };
    }

    private static bool Connect(zkemkeeper.CZKEMClass zk, string name, Options o)
    {
        var started = DateTime.UtcNow;
        var ok = zk.Connect_Net(o.Ip, o.Port);
        var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
        if (!ok)
        {
            Log($"{name}: Connect_Net falló en {elapsed:F0} ms, error {LastError(zk)}");
            return false;
        }
        Log($"{name}: conectado en {elapsed:F0} ms");
        return true;
    }

    private static void Register(zkemkeeper.CZKEMClass zk, string name)
    {
        // 65535: todos los eventos (marcaje, verificación, huella, teclado, etc.).
        var ok = zk.RegEvent(Machine, 65535);
        Log($"{name}: RegEvent(65535) -> {ok}{(ok ? "" : $", error {LastError(zk)}")}");
    }

    private static void PollOnce(zkemkeeper.CZKEMClass zk, string name, Stats stats)
    {
        var started = Environment.TickCount64;
        var read = zk.ReadRTLog(Machine);
        var elapsed = Environment.TickCount64 - started;
        var outcome = read ? "ReadRTLog=true" : $"ReadRTLog=false/err {LastError(zk)}";
        stats.Count(outcome);
        if (elapsed > 1500) Log($"{name}: {outcome} tardó {elapsed} ms");
        if (!read) return;

        // GetRTLog entrega un evento por llamada y dispara su manejador en este hilo.
        var drained = 0;
        while (zk.GetRTLog(Machine) && drained < 100) drained++;
        if (drained > 0)
        {
            stats.Count($"GetRTLog entregó {drained}");
        }
    }

    private static bool ReadTime(zkemkeeper.CZKEMClass zk, string name, bool quiet = false)
    {
        int year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0;
        var started = Environment.TickCount64;
        var ok = zk.GetDeviceTime(Machine, ref year, ref month, ref day, ref hour, ref minute, ref second);
        var elapsed = Environment.TickCount64 - started;
        if (!ok) Log($"{name}: GetDeviceTime falló en {elapsed} ms, error {LastError(zk)}");
        else if (!quiet) Log($"{name}: hora del reloj {year:0000}-{month:00}-{day:00} {hour:00}:{minute:00}:{second:00} ({elapsed} ms)");
        return ok;
    }

    private static void Disconnect(zkemkeeper.CZKEMClass zk, string name)
    {
        zk.Disconnect();
        Log($"{name}: desconectado");
    }

    private static int LastError(zkemkeeper.CZKEMClass zk)
    {
        var code = 0;
        zk.GetLastError(ref code);
        return code;
    }

    /// <summary>
    /// En STA bombea mensajes mientras espera (por si el SDK entrega eventos por mensajes de ventana); en MTA duerme.
    /// </summary>
    private static void Wait(bool sta, int milliseconds)
    {
        if (!sta)
        {
            Thread.Sleep(milliseconds);
            return;
        }

        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until)
        {
            while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            Thread.Sleep(10);
        }
    }

    private static void Log(string message) =>
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [h{Environment.CurrentManagedThreadId} {Thread.CurrentThread.GetApartmentState()}] {message}");

    private sealed class Stats
    {
        private readonly Dictionary<string, int> _counts = new();
        public int Events;
        public int Disconnects;

        public void Count(string key) => _counts[key] = _counts.GetValueOrDefault(key) + 1;

        public void Print()
        {
            Log($"Resumen: {Events} marcajes recibidos, {Disconnects} desconexiones");
            foreach (var (key, count) in _counts.OrderByDescending(c => c.Value)) Log($"  {key}: {count}");
        }
    }

    private static Options Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("listen" or "second" or "watch")) throw new ArgumentException("Falta el modo: listen | second | watch");

        string? ip = null;
        int port = 4370, seconds = 120, pollMs = 1000, opEvery = 0, secondAt = 20, watch = 5;
        bool sta = false, poll = true, crossThread = false;
        for (var i = 1; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Falta el valor de {args[i]}");
            int NextInt() => int.Parse(Next(), CultureInfo.InvariantCulture);
            switch (args[i])
            {
                case "--ip": ip = Next(); break;
                case "--port": port = NextInt(); break;
                case "--seconds": seconds = NextInt(); break;
                case "--sta": sta = true; break;
                case "--no-poll": poll = false; break;
                case "--poll-ms": pollMs = NextInt(); break;
                case "--op-every": opEvery = NextInt(); break;
                case "--op-cross-thread": crossThread = true; break;
                case "--second-at": secondAt = NextInt(); break;
                case "--watch-every": watch = NextInt(); break;
                default: throw new ArgumentException($"Opción desconocida: {args[i]}");
            }
        }
        if (ip == null) throw new ArgumentException("Falta --ip");
        return new Options(args[0], ip, port, seconds, sta, poll, pollMs, opEvery, crossThread, secondAt, watch);
    }

    private static void PrintUsage() => Console.Error.WriteLine(
        "Uso: RealtimeProbe listen|second|watch --ip <ip> [--port 4370] [--seconds 120] [--sta] [--no-poll] [--poll-ms 1000]\n" +
        "                     [--op-every <s>] [--op-cross-thread] [--second-at 20] [--watch-every 5]");

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg msg);
}
