using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using zkemkeeper;
using FormsTimer = System.Windows.Forms.Timer;

namespace ZKTecoRealtimeProbe;

/// <summary>
/// Un reloj = un hilo STA con su propio bucle de mensajes. El objeto COM se crea, se usa y recibe sus eventos
/// únicamente en ese hilo; todo lo que venga de fuera (consola, apagado) se encola con <see cref="Post"/>.
/// </summary>
internal sealed class DeviceProbe
{
    private static readonly int[] ReconnectDelaysSeconds = { 5, 10, 30, 60 };

    private readonly DeviceEndpoint _endpoint;
    private readonly ProbeOptions _options;
    private readonly ProbeLog _log;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();

    private SynchronizationContext? _context;
    private CZKEMClass? _device;
    private FormsTimer? _heartbeatTimer;
    private FormsTimer? _reconnectTimer;

    private bool _connected;
    private bool _stopping;
    private int _reconnectAttempt;
    private int _heartbeatFailures;
    private DateTime? _connectedAt;
    private DateTime? _disconnectedAt;
    private DateTime? _lastDeviceTime;
    private TimeSpan? _clockDrift;
    private int _punchEvents;
    private int _otherEvents;
    private int _disconnections;

    public DeviceProbe(DeviceEndpoint endpoint, ProbeOptions options, ProbeLog log)
    {
        _endpoint = endpoint;
        _options = options;
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = $"zk-{endpoint}" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    private string Name => _endpoint.ToString();
    private int Machine => _options.MachineNumber;

    public void Start()
    {
        _thread.Start();
        _ready.Wait();
    }

    public void Stop()
    {
        Post(() =>
        {
            _stopping = true;
            _heartbeatTimer?.Stop();
            _reconnectTimer?.Stop();
            if (_connected)
            {
                _connected = false;
                _device?.Disconnect();
                _log.Info(Name, "Desconectado (fin de la prueba).");
            }
            Application.ExitThread();
        });

        if (!_thread.Join(TimeSpan.FromSeconds(15)))
            _log.Warn(Name, "El hilo del reloj no terminó en 15 s.");
    }

    public void Post(Action action)
    {
        var context = _context;
        if (context == null)
        {
            _log.Warn(Name, "El hilo del reloj no está activo; se ignora el comando.");
            return;
        }

        context.Post(_ =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _log.Error(Name, $"Error no controlado en el hilo del reloj: {ex}");
            }
        }, null);
    }

    // ===== Hilo STA =====

    private void Run()
    {
        // Crea el control de serialización de WinForms en este hilo: Post() encola en su bucle de mensajes.
        var context = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);

        _log.Info(Name, $"Hilo {Thread.CurrentThread.ManagedThreadId} en apartamento {Thread.CurrentThread.GetApartmentState()}.");

        try
        {
            _device = new CZKEMClass();
        }
        catch (COMException ex)
        {
            _log.Error(Name, $"No se pudo crear CZKEMClass (0x{ex.HResult:X8}). ¿Está registrado zkemkeeper.dll con el regsvr32 de 32 bits? {ex.Message}");
            _ready.Set();
            return;
        }

        Subscribe(_device);

        _heartbeatTimer = new FormsTimer { Interval = _options.HeartbeatSeconds * 1000 };
        _heartbeatTimer.Tick += (_, _) => Heartbeat();
        _reconnectTimer = new FormsTimer();
        _reconnectTimer.Tick += (_, _) =>
        {
            _reconnectTimer.Stop();
            Connect();
        };

        _context = context;
        _ready.Set();

        Post(Connect);
        Application.Run();

        _heartbeatTimer.Dispose();
        _reconnectTimer.Dispose();
        Marshal.FinalReleaseComObject(_device);
        _log.Info(Name, "Hilo del reloj terminado.");
    }

    private void Subscribe(CZKEMClass device)
    {
        // El firmware dispara una sola de las tres variantes; registrar cuál es parte del resultado de la prueba.
        device.OnAttTransactionEx += (enroll, invalid, state, verify, y, mo, d, h, mi, s, workCode) =>
            OnPunch("OnAttTransactionEx", enroll, invalid, state, verify, y, mo, d, h, mi, s, workCode.ToString(CultureInfo.InvariantCulture));
        device.OnAttTransactionEx_New += (enroll, invalid, state, verify, y, mo, d, h, mi, s, workCode) =>
            OnPunch("OnAttTransactionEx_New", enroll, invalid, state, verify, y, mo, d, h, mi, s, workCode);
        device.OnAttTransaction += (enroll, invalid, state, verify, y, mo, d, h, mi, s) =>
            OnPunch("OnAttTransaction", enroll.ToString(CultureInfo.InvariantCulture), invalid, state, verify, y, mo, d, h, mi, s, "");

        device.OnConnected += () => OtherEvent("OnConnected", "");
        device.OnDisConnected += () =>
        {
            OtherEvent("OnDisConnected", "");
            MarkDisconnected("evento OnDisConnected");
        };

        device.OnVerify += userId => OtherEvent("OnVerify", $"usuario={userId}");
        device.OnFinger += () => OtherEvent("OnFinger", "");
        device.OnKeyPress += key => OtherEvent("OnKeyPress", $"tecla={key}");
        device.OnAlarm += (type, enroll, verified) => OtherEvent("OnAlarm", $"tipo={type} usuario={enroll} verificado={verified}");
        device.OnDoor += type => OtherEvent("OnDoor", $"tipo={type}");
        device.OnNewUser += enroll => OtherEvent("OnNewUser", $"usuario={enroll}");
        device.OnGeneralEvent += data => OtherEvent("OnGeneralEvent", data);
    }

    // ===== Conexión, latido y reconexión =====

    private void Connect()
    {
        if (_stopping || _connected || _device == null) return;

        var watch = Stopwatch.StartNew();
        if (_options.CommKey is { } commKey) _device.SetCommPassword(commKey);

        if (!_device.Connect_Net(_endpoint.IpAddress, _endpoint.Port))
        {
            _log.Error(Name, $"Connect_Net falló tras {watch.ElapsedMilliseconds} ms (código SDK {LastError()}).");
            ScheduleReconnect();
            return;
        }

        var connectMs = watch.ElapsedMilliseconds;
        var registered = _device.RegEvent(Machine, _options.EventMask);
        var registerError = registered ? 0 : LastError();

        _connected = true;
        _connectedAt = DateTime.Now;
        _reconnectAttempt = 0;
        _heartbeatFailures = 0;

        if (registered)
            _log.Success(Name, $"Conectado en {connectMs} ms; RegEvent(máscara {_options.EventMask}) aceptado. Esperando checadas…");
        else
            _log.Error(Name, $"Conectado en {connectMs} ms pero RegEvent falló (código SDK {registerError}): no llegarán eventos.");

        var gapStart = _disconnectedAt.HasValue ? _lastDeviceTime : null;
        var gapStartedAt = _disconnectedAt;
        _disconnectedAt = null;

        ReadDeviceClock(logAlways: true);
        _heartbeatTimer!.Start();

        if (gapStart.HasValue && gapStartedAt.HasValue)
        {
            _log.Info(Name, $"Hueco sin conexión de {Describe(DateTime.Now - gapStartedAt.Value)}.");
            if (_options.CatchUpAfterReconnect && _lastDeviceTime.HasValue)
                ReadLogs("recuperación", gapStart.Value.AddMinutes(-1), _lastDeviceTime.Value);
        }
    }

    private void Heartbeat()
    {
        if (!_connected) return;

        if (ReadDeviceClock(logAlways: _options.Verbose))
        {
            _heartbeatFailures = 0;
            return;
        }

        _heartbeatFailures++;
        _log.Warn(Name, $"Latido sin respuesta ({_heartbeatFailures}/{_options.HeartbeatFailuresToDisconnect}), código SDK {LastError()}.");
        if (_heartbeatFailures >= _options.HeartbeatFailuresToDisconnect)
            MarkDisconnected("latido sin respuesta");
    }

    private bool ReadDeviceClock(bool logAlways)
    {
        int y = 0, mo = 0, d = 0, h = 0, mi = 0, s = 0;
        var watch = Stopwatch.StartNew();
        if (!_device!.GetDeviceTime(Machine, ref y, ref mo, ref d, ref h, ref mi, ref s)) return false;

        var deviceTime = new DateTime(y, mo, d, h, mi, s);
        _lastDeviceTime = deviceTime;
        _clockDrift = deviceTime - DateTime.Now;

        if (logAlways)
            _log.Info(Name, $"Hora del reloj {deviceTime:yyyy-MM-dd HH:mm:ss}; desfase vs PC {Signed(_clockDrift.Value)}; ida y vuelta {watch.ElapsedMilliseconds} ms.");
        return true;
    }

    private void MarkDisconnected(string reason)
    {
        if (!_connected || _stopping) return;

        _connected = false;
        _disconnections++;
        _disconnectedAt = DateTime.Now;
        _heartbeatTimer!.Stop();

        var duration = _connectedAt.HasValue ? Describe(DateTime.Now - _connectedAt.Value) : "?";
        _log.Warn(Name, $"Conexión caída ({reason}) tras {duration} conectado. Último latido correcto: {_lastDeviceTime:HH:mm:ss} (hora del reloj).");

        try
        {
            _device!.Disconnect();
        }
        catch (COMException ex)
        {
            _log.Warn(Name, $"Disconnect lanzó 0x{ex.HResult:X8}.");
        }

        ScheduleReconnect();
    }

    private void ScheduleReconnect()
    {
        if (_stopping) return;

        var delay = ReconnectDelaysSeconds[Math.Min(_reconnectAttempt, ReconnectDelaysSeconds.Length - 1)];
        _reconnectAttempt++;
        _log.Info(Name, $"Reintento de conexión #{_reconnectAttempt} en {delay} s.");
        _reconnectTimer!.Interval = delay * 1000;
        _reconnectTimer.Start();
    }

    // ===== Eventos =====

    private void OnPunch(string eventName, string enroll, int invalid, int state, int verify,
        int y, int mo, int d, int h, int mi, int s, string workCode)
    {
        var receivedAt = DateTime.Now;
        _punchEvents++;

        DateTime punchTime;
        try
        {
            punchTime = new DateTime(y, mo, d, h, mi, s);
        }
        catch (ArgumentOutOfRangeException)
        {
            _log.Error(Name, $"{eventName}: fecha inválida {y}-{mo}-{d} {h}:{mi}:{s} para usuario {enroll}.");
            return;
        }

        var lag = receivedAt - punchTime;
        var latency = _clockDrift is { } drift ? $"; latencia estimada {Signed(lag + drift)}" : "";
        var validity = invalid == 0 ? "válida" : "VERIFICACIÓN FALLIDA (se descartaría)";
        _log.Punch(Name,
            $"{eventName}: usuario {enroll} a las {punchTime:yyyy-MM-dd HH:mm:ss} ({validity}); estado {state}, verificación {verify}, workcode '{workCode}'; atraso vs PC {Signed(lag)}{latency}.");
        _log.PunchRow(receivedAt, Name, "evento", eventName, enroll, punchTime, invalid, state, verify, workCode, _clockDrift);
    }

    private void OtherEvent(string eventName, string detail)
    {
        _otherEvents++;
        _log.Event(Name, string.IsNullOrEmpty(detail) ? eventName : $"{eventName}: {detail}");
    }

    // ===== Lecturas bajo demanda (comandos de consola) =====

    public void ReadLastWindow() => Post(() =>
    {
        if (!RequireConnected() || !ReadDeviceClock(logAlways: false)) return;
        var to = _lastDeviceTime!.Value;
        ReadLogs($"ventana de {_options.ReadWindowMinutes} min", to.AddMinutes(-_options.ReadWindowMinutes), to);
    });

    public void ReadNew() => Post(() =>
    {
        if (!RequireConnected()) return;
        RunRead("ReadNewGLogData", () => _device!.ReadNewGLogData(Machine), listRecords: true);
    });

    public void ReadAllCountOnly() => Post(() =>
    {
        if (!RequireConnected()) return;
        RunRead("ReadAllGLogData", () => _device!.ReadAllGLogData(Machine), listRecords: false);
    });

    public void Status() => Post(() =>
    {
        var state = _connected
            ? $"conectado desde {_connectedAt:HH:mm:ss}"
            : _disconnectedAt.HasValue ? $"DESCONECTADO desde {_disconnectedAt:HH:mm:ss}" : "sin conectar";
        var drift = _clockDrift is { } d ? Signed(d) : "?";
        _log.Info(Name, $"Estado: {state}; checadas por evento {_punchEvents}; otros eventos {_otherEvents}; caídas {_disconnections}; desfase {drift}.");
    });

    private void ReadLogs(string label, DateTime from, DateTime to)
    {
        var fromText = from.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var toText = to.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        RunRead($"ReadTimeGLogData {label} [{fromText} – {toText}]",
            () => _device!.ReadTimeGLogData(Machine, fromText, toText), listRecords: true);
    }

    /// <summary>
    /// Igual que la descarga de producción: deshabilita el reloj mientras lee. Se mide cuánto tiempo queda bloqueado,
    /// que es lo que decide si la alternativa de sondeo frecuente es viable.
    /// </summary>
    private void RunRead(string label, Func<bool> read, bool listRecords)
    {
        var watch = Stopwatch.StartNew();
        _device!.EnableDevice(Machine, false);
        try
        {
            if (!read())
            {
                var code = LastError();
                var hint = code == 0 ? " (normalmente: sin registros en el rango)" : code == -4 ? " (posiblemente no soportado por el firmware)" : "";
                _log.Warn(Name, $"{label}: devolvió false, código SDK {code}{hint}; reloj bloqueado {watch.ElapsedMilliseconds} ms.");
                return;
            }

            int count = 0;
            int workCode = 0;
            while (_device.SSR_GetGeneralLogData(Machine, out var userId, out var verify, out var inOut,
                       out var y, out var mo, out var d, out var h, out var mi, out var s, ref workCode))
            {
                count++;
                if (!listRecords) continue;

                var punchTime = new DateTime(y, mo, d, h, mi, s);
                _log.Punch(Name, $"  {label}: usuario {userId} a las {punchTime:yyyy-MM-dd HH:mm:ss}; estado {inOut}, verificación {verify}.");
                _log.PunchRow(DateTime.Now, Name, "lectura", label.Split(' ')[0], userId, punchTime, 0, inOut, verify,
                    workCode.ToString(CultureInfo.InvariantCulture), _clockDrift);
            }

            _log.Info(Name, $"{label}: {count} registros; reloj bloqueado {watch.ElapsedMilliseconds} ms.");
        }
        finally
        {
            _device.EnableDevice(Machine, true);
        }
    }

    private bool RequireConnected()
    {
        if (_connected) return true;
        _log.Warn(Name, "No conectado; se ignora el comando.");
        return false;
    }

    private int LastError()
    {
        int code = 0;
        _device?.GetLastError(ref code);
        return code;
    }

    private static string Signed(TimeSpan span) =>
        (span < TimeSpan.Zero ? "-" : "+") + span.Duration().TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    private static string Describe(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min"
        : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} min {span.Seconds} s"
        : $"{span.TotalSeconds:0} s";
}
