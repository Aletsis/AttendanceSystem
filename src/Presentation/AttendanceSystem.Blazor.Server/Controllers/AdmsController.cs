using Microsoft.AspNetCore.Mvc;
using MediatR;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;
using AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using Microsoft.Extensions.Logging;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using System.Globalization;

namespace AttendanceSystem.Blazor.Server.Controllers;

[Route("iclock")]
[Route("")]
[ApiController]
public class AdmsController : ControllerBase
{
    private readonly ILogger<AdmsController> _logger;
    private readonly IMediator _mediator;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IDownloadLogRepository _downloadLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAdmsCommandService _admsCommandService;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly ILogTransferService _logTransferService;
    private readonly IAttendanceJobScheduler _jobScheduler;

    public AdmsController(
        ILogger<AdmsController> logger,
        IMediator mediator,
        IDeviceRepository deviceRepository,
        IUnitOfWork unitOfWork,
        IAdmsCommandService admsCommandService,
        IEmployeeRepository employeeRepository,
        IDownloadLogRepository downloadLogRepository,
        IBranchRepository branchRepository,
        ILogTransferService logTransferService,
        IAttendanceJobScheduler jobScheduler)
    {
        _logger = logger;
        _mediator = mediator;
        _deviceRepository = deviceRepository;
        _unitOfWork = unitOfWork;
        _admsCommandService = admsCommandService;
        _downloadLogRepository = downloadLogRepository;
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _logTransferService = logTransferService;
        _jobScheduler = jobScheduler;
    }

    // 1. GET /iclock/cdata — opciones de configuración o verificación de registro
    [HttpGet("cdata")]
    public async Task<IActionResult> CheckData(
        [FromQuery] string SN,
        [FromQuery] string? options = null,
        [FromQuery] string? PushOptionsFlag = null,
        [FromQuery] string? pushver = null)
    {
        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";
        _logger.LogInformation("🔧 GET cdata {SN} (options: {Options}, pushver: {PushVer}, PushOptionsFlag: {PushOptionsFlag})",
            SN, options ?? "null", pushver ?? "null", PushOptionsFlag ?? "null");

        if (!string.IsNullOrEmpty(SN))
        {
            var device = await _deviceRepository.GetBySerialNumberAsync(SN);
            if (device != null && device.Status != DeviceStatus.Online)
            {
                device.MarkAsOnline();
                await _deviceRepository.UpdateAsync(device);
                await _unitOfWork.SaveChangesAsync();
            }
        }

        // Si el dispositivo solicita las opciones del servidor (ej. tras CHECK o al inicializar pushver 3.x)
        if (!string.IsNullOrEmpty(options) || !string.IsNullOrEmpty(PushOptionsFlag))
        {
            var optionsResponse = await BuildPushOptionsResponseAsync(SN);
            return Content(optionsResponse, "text/plain;charset=ISO-8859-1");
        }

        // Registro inicial simple
        return Content("registry=ok\n", "text/plain");
    }

    // 2. POST /iclock/registry — responder SOLO el RegistryCode
    [HttpPost("registry")]
    public async Task<IActionResult> Registry([FromQuery] string SN)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        _logger.LogInformation("🤝 Registry {SN}", SN);

        // Parsear DeviceType del body
        var fields = body.Split(',')
            .Select(f => f.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

        fields.TryGetValue("DeviceType", out var deviceType); // "acc" o "att"

        // Persistir en tu entidad de dispositivo
        var device = await _deviceRepository.GetBySerialNumberAsync(SN);
        if (device != null)
        {
            device.SetDeviceType(deviceType ?? "att");
            device.MarkAsOnline();
            await _deviceRepository.UpdateAsync(device);
            await _unitOfWork.SaveChangesAsync();
        }

        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";

        // Manual página 32: respuesta normal es SOLO RegistryCode
        var registryCode = Guid.NewGuid().ToString("N")[..10].ToUpper();
        return Content($"RegistryCode={registryCode}\n", "text/plain;charset=ISO-8859-1");
    }

    // 3. POST /iclock/push — aquí va la configuración completa (sección 7.5 del manual)
    [HttpPost("push")]
    public async Task<IActionResult> Push([FromQuery] string SN)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        _logger.LogInformation("📥 POST push {SN}", SN);

        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";

        var response = await BuildPushOptionsResponseAsync(SN);
        return Content(response, "text/plain;charset=ISO-8859-1");
    }

    private async Task<string> BuildPushOptionsResponseAsync(string SN)
    {
        var device = await _deviceRepository.GetBySerialNumberAsync(SN);
        if (device != null && device.Status != DeviceStatus.Online)
        {
            device.MarkAsOnline();
            await _deviceRepository.UpdateAsync(device);
            await _unitOfWork.SaveChangesAsync();
        }

        var isAccessMode = device?.DeviceType == "acc";

        // Obtener último timestamp de logs para este dispositivo (formato estándar: yyyy-MM-dd HH:mm:ss o 0 para resincronización completa)
        var lastLog = await _deviceRepository.GetLastAttLogTimestampAsync(SN);
        var stamp = lastLog.HasValue
            ? lastLog.Value.ToString("yyyy-MM-dd HH:mm:ss")
            : "0";

        // TransTables compatible con ambos modos (Transaction y User Transaction)
        var transTable = "Transaction,User Transaction,User,UserPic,BioData,Fingerprint,Face,USERINFO,USERPIC,BIODATA";

        var tzOffsetHours = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalHours;

        // Manual sección 7.5: configuración y stamps de push (compatible con modo att y acc, Push 2.0 y 3.x)
        var sessionId = Guid.NewGuid().ToString("N").ToUpper();
        var response = $"GET OPTION FROM: {SN}\n" +
                       $"Stamp={stamp}\n" +
                       $"TransStamp={stamp}\n" +
                       $"ATTLOGStamp={stamp}\n" +
                       $"OpStamp=0\n" +
                       $"OPERLOGStamp=0\n" +
                       $"PhotoStamp=0\n" +
                       $"ATTPHOTOStamp=0\n" +
                       $"TimeZone={tzOffsetHours}\n" +
                       $"~TimeZone={tzOffsetHours}\n" +
                       $"TZ={tzOffsetHours}\n" +
                       $"DaylightSavingTime=0\n" +
                       $"~DaylightSavingTime=0\n" +
                       $"ErrorDelay=60\n" +
                       $"Delay=10\n" +
                       $"RequestDelay=5\n" +
                       $"TransTimes=00:00;14:00\n" +
                       $"TransInterval=1\n" +
                       $"TransTables={transTable}\n" +
                       $"Realtime=1\n" +
                       $"SessionID={sessionId}\n" +
                       $"TimeoutSec=10\n" +
                       $"ServerVersion=3.1.2\n" +
                       $"ServerName=ADMS\n" +
                       $"PushVersion=3.1.2\n";

        _logger.LogInformation("📋 Push Options {SN} stamp={Stamp} ({StampReadable}) mode={Mode}",
            SN, stamp,
            lastLog.HasValue ? lastLog.Value.ToString("yyyy-MM-dd HH:mm:ss") : "0 (Full Sync)",
            isAccessMode ? "acc" : "att");

        return response;
    }

    // 4. POST /iclock/cdata — recibir datos (ATTLOG, rtlog, options, etc.)
    [HttpPost("cdata")]
    public async Task<IActionResult> ReceiveData([FromQuery] string SN, [FromQuery] string? table = null)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        _logger.LogInformation("📥 POST cdata SN:{SN} table:{Table}", SN, table);

        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";

        // ATTLOG y rtlog (Real-time log) contienen checadas de asistencia
        // "Transaction" contiene logs históricos cuando está en modo acceso (DeviceType=acc)
        if (table?.Equals("ATTLOG", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("rtlog", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("Transaction", StringComparison.OrdinalIgnoreCase) == true)
        {
            var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            await ProcessAttLogs(lines, SN);
            return Content($"OK: {lines.Length}", "text/plain");
        }

        if (table?.Equals("USER", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("USERINFO", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("USERPIC", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("BIODATA", StringComparison.OrdinalIgnoreCase) == true)
        {
            var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            await ProcessDeviceData(lines, SN, table);
            return Content("OK", "text/plain");
        }

        if (table?.Equals("options", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("tabledata", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogInformation("📋 [ADMS DIAGNÓSTICO] Opciones/Parámetros recibidos en cdata para SN: {SN}:\n{Body}", SN, body);
            return Content("OK", "text/plain");
        }

        // rtstate, tabledata, etc. — responder OK
        return Content("OK", "text/plain");
    }

    // 4.1 POST /iclock/querydata — recibir respuestas a consultas DATA QUERY (ATTLOG, Transaction, USERINFO, etc.)
    [HttpPost("querydata")]
    public async Task<IActionResult> QueryData(
        [FromQuery] string SN,
        [FromQuery] string? tablename = null,
        [FromQuery] string? table = null,
        [FromQuery] string? type = null)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var targetTable = tablename ?? table ?? "";
        _logger.LogInformation("📥 POST querydata SN:{SN} table:{Table} type:{Type}", SN, targetTable, type);

        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";

        if (targetTable.Equals("options", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("tabledata", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("📋 [ADMS DIAGNÓSTICO] Opciones/Parámetros recibidos en querydata para SN: {SN}:\n{Body}", SN, body);
            return Content("OK", "text/plain");
        }

        if (targetTable.Equals("ATTLOG", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("Transaction", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("User Transaction", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("USERTRANSACTION", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("rtlog", StringComparison.OrdinalIgnoreCase))
        {
            var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var count = await ProcessAttLogs(lines, SN);
            return Content($"OK: {count}", "text/plain");
        }

        if (targetTable.Equals("USER", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("USERINFO", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("USERPIC", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("BIODATA", StringComparison.OrdinalIgnoreCase))
        {
            var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            await ProcessDeviceData(lines, SN, targetTable);
            return Content("OK", "text/plain");
        }

        return Content("OK", "text/plain");
    }

    // 5. GET /iclock/getrequest — heartbeat y comandos
    [HttpGet("getrequest")]
    public async Task<IActionResult> GetRequest([FromQuery] string SN)
    {
        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";
        _logger.LogInformation("🔔 getrequest {SN}", SN);

        if (!string.IsNullOrEmpty(SN))
        {
            var device = await _deviceRepository.GetBySerialNumberAsync(SN);
            if (device != null && device.Status != DeviceStatus.Online)
            {
                device.MarkAsOnline();
                await _deviceRepository.UpdateAsync(device);
                await _unitOfWork.SaveChangesAsync();
            }
        }

        if (_admsCommandService.HasPendingCommands(SN))
        {
            var (command, logId) = _admsCommandService.GetNextCommand(SN);
            if (!string.IsNullOrEmpty(command))
            {
                var cmdId = new Random().Next(1000, 9999).ToString();
                if (logId.HasValue)
                    _admsCommandService.RegisterPendingExecution(SN, cmdId, logId.Value);

                _logger.LogInformation("📤 Comando → {SN}: {Command}", SN, command);
                return Content($"C:{cmdId}:{command}\n", "text/plain;charset=ISO-8859-1");
            }
        }

        return Content("OK", "text/plain;charset=ISO-8859-1");
    }

    // Diagnostic endpoint to check pending commands
    [HttpGet("debug/pending-commands")]
    public IActionResult GetPendingCommandsDebug([FromQuery] string? SN = null)
    {
        if (!string.IsNullOrWhiteSpace(SN))
        {
            var hasPending = _admsCommandService.HasPendingCommands(SN);
            return Ok(new { SerialNumber = SN, HasPendingCommands = hasPending });
        }

        return Ok(new { Message = "Provide SN parameter to check specific device" });
    }

    // Ping endpoint for connectivity check
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Content("OK\r\n", "text/plain");
    }

    [HttpPost("devicecmd")]
    public async Task<IActionResult> DeviceCmd([FromQuery] string? SN = null)
    {
        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";

        // El reloj puede mandar SN, ID, Return en query string O en el body
        string sn = SN ?? "";
        string id = "", ret = "", cmd = "";
        string body = "";

        try
        {
            using var reader = new StreamReader(Request.Body);
            body = await reader.ReadToEndAsync();

            id = Request.Query["ID"].FirstOrDefault() ?? "";
            ret = Request.Query["Return"].FirstOrDefault() ?? "";
            cmd = Request.Query["CMD"].FirstOrDefault() ?? "";
            if (string.IsNullOrEmpty(sn))
                sn = Request.Query["SN"].FirstOrDefault() ?? "";

            // Parsear si vienen en body como form-encoded
            if (body.Contains("=") && (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(ret)))
            {
                var parsed = System.Web.HttpUtility.ParseQueryString(body);
                if (string.IsNullOrEmpty(id)) id = parsed["ID"] ?? "";
                if (string.IsNullOrEmpty(ret)) ret = parsed["Return"] ?? "";
                if (string.IsNullOrEmpty(cmd)) cmd = parsed["CMD"] ?? "";
                if (string.IsNullOrEmpty(sn)) sn = parsed["SN"] ?? "";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error leyendo request en DeviceCmd");
        }

        _logger.LogInformation("📬 devicecmd SN:{SN} ID:{ID} Return:{Return} CMD:{CMD}", sn, id, ret, cmd);

        // Si el body contiene registros de asistencia (en formato TSV o key=value) devueltos por DATA QUERY
        if (!string.IsNullOrEmpty(sn) && (body.Contains("\t") || body.Contains("pin=") || body.Contains("PIN=")))
        {
            var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !l.StartsWith("ID=", StringComparison.OrdinalIgnoreCase) &&
                            !l.StartsWith("Return=", StringComparison.OrdinalIgnoreCase) &&
                            !l.StartsWith("CMD=", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (lines.Length > 0)
            {
                _logger.LogInformation("📥 ADMS: Procesando {Count} registros recibidos en cuerpo de devicecmd para SN: {SN}", lines.Length, sn);
                await ProcessAttLogs(lines, sn);
            }
        }

        try
        {
            if (!string.IsNullOrEmpty(id))
            {
                var logId = _admsCommandService.GetAndRemovePendingExecution(id);
                if (logId.HasValue)
                {
                    var log = await _downloadLogRepository.GetByIdAsync(new AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLogId(logId.Value));
                    if (log != null)
                    {
                        var returnCode = int.TryParse(ret, out var r) ? r : -1;

                        if (returnCode >= 0 || returnCode == -2)
                        {
                            // Return >= 0 o -2 = éxito o sin datos nuevos en el rango
                            log.MarkAsSuccessful(0, 0);

                            // Actualizar LastDownloadAt del dispositivo usando el ToDate solicitado
                            var device = await _deviceRepository.GetBySerialNumberAsync(sn);
                            if (device != null && log.ToDate.HasValue)
                            {
                                device.RecordSuccessfulDownload(0, log.ToDate);
                                await _deviceRepository.UpdateAsync(device);
                            }

                            _logger.LogInformation("✅ ADMS: Descarga completada SN:{SerialNumber} CMD:{CommandId}", sn, id);
                        }
                        else
                        {
                            // Fallo reportado por el dispositivo con mapeo amigable de códigos ZKTeco
                            var friendlyError = returnCode switch
                            {
                                -1 => "Error general en la ejecución interna del reloj (código -1).",
                                -5 => "El reloj está ocupado procesando otra tarea o biometría (código -5).",
                                -629 => "Tabla o parámetros de consulta no soportados por el firmware del reloj (código -629).",
                                -1001 => "Memoria de almacenamiento llena en el dispositivo (código -1001).",
                                -1002 => "Sintaxis de comando no válida o comando no reconocido por esta versión de firmware (código -1002).",
                                _ => $"El dispositivo reportó error con código de retorno: {ret}"
                            };

                            log.MarkAsFailed(friendlyError);
                            _logger.LogWarning("❌ ADMS: Dispositivo {SerialNumber} reportó error {ReturnCode} ({FriendlyError}) para comando {CommandId}", sn, ret, friendlyError, id);
                        }

                        await _downloadLogRepository.UpdateAsync(log);
                        await _unitOfWork.SaveChangesAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing DeviceCmd for SN: {SN}, ID: {ID}", sn, id);
        }
        return Content("OK", "text/plain");
    }
    private async Task<int> ProcessAttLogs(string[] lines, string SN)
    {
        var device = await _deviceRepository.GetBySerialNumberAsync(SN);
        if (device == null) return 0;
        int processed = 0;
        DateTime? lastCheckTime = null;
        var uniqueCalculations = new HashSet<(string Pin, DateTime Date)>();
        var localLogs = new List<AttendanceLogDto>();

        foreach (var line in lines)
        {
            try
            {
                var parts = line.Split('\t');

                string? pin = "";
                DateTime checkTime = DateTime.MinValue;
                int checkType = 0;
                int verifyMethod = 3;
                bool isValid = false;

                // El formato en modo acceso u opciones de rtlog viene como "time=xxx\tpin=xxx"
                // mientras que en ATTLOG clásico es por posiciones "78\t2022-11-14 13:46:27\t0\t15"
                if (line.Contains("=") || line.Contains("time", StringComparison.OrdinalIgnoreCase) || line.Contains("pin", StringComparison.OrdinalIgnoreCase))
                {
                    var logData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var tokens = line.Contains('\t') ? line.Split('\t') : line.Split(',');
                    foreach (var part in tokens)
                    {
                        var kvp = part.Split('=', 2);
                        if (kvp.Length == 2)
                        {
                            logData[kvp[0].Trim()] = kvp[1].Trim();
                        }
                    }

                    // Extraer PIN / Identificador de empleado
                    if (!logData.TryGetValue("pin", out pin) || string.IsNullOrWhiteSpace(pin))
                    {
                        if (!logData.TryGetValue("user_id", out pin) || string.IsNullOrWhiteSpace(pin))
                        {
                            if (!logData.TryGetValue("userid", out pin) || string.IsNullOrWhiteSpace(pin))
                            {
                                logData.TryGetValue("cardno", out pin);
                            }
                        }
                    }

                    // Extraer fecha y hora de la checada
                    if (!logData.TryGetValue("time", out var timeStr) || string.IsNullOrWhiteSpace(timeStr))
                    {
                        if (!logData.TryGetValue("time_second", out timeStr) || string.IsNullOrWhiteSpace(timeStr))
                        {
                            logData.TryGetValue("logtime", out timeStr);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(pin) && !string.IsNullOrWhiteSpace(timeStr) && DateTime.TryParse(timeStr, out checkTime))
                    {
                        // En Access Control (Transaction / rtlog):
                        // 0..19 y 200..255: Eventos normales / verificaciones concedidas (0=Normal, 3=Multi/Punch normal, 14=Normal verify, etc.)
                        // 20..99: Acceso denegado / Errores (tarjeta no válida, PIN incorrecto, fuera de horario)
                        // 100..199: Alarmas (puerta abierta, coacción, sabotaje)
                        string? eventStr = null;
                        if (logData.TryGetValue("event", out eventStr) ||
                            logData.TryGetValue("eventtype", out eventStr) ||
                            logData.TryGetValue("event_type", out eventStr))
                        {
                            if (int.TryParse(eventStr, out var eventCode))
                            {
                                bool isDeniedOrAlarm = (eventCode >= 20 && eventCode <= 199);
                                if (isDeniedOrAlarm)
                                {
                                    _logger.LogInformation("ADMS: Evento de acceso denegado o alarma ignorado (EventCode={EventCode}, PIN={Pin}, SN={SN})", eventCode, pin, SN);
                                    continue;
                                }
                            }
                        }

                        // Intentar sacar status y método
                        string? inoutStr = null;
                        if (logData.TryGetValue("inoutstatus", out inoutStr) ||
                            logData.TryGetValue("inoutstate", out inoutStr) ||
                            logData.TryGetValue("state", out inoutStr) ||
                            logData.TryGetValue("status", out inoutStr))
                        {
                            if (int.TryParse(inoutStr, out var s))
                                checkType = s;
                        }

                        string? vTypeStr = null;
                        if (logData.TryGetValue("verifytype", out vTypeStr) ||
                            logData.TryGetValue("verified", out vTypeStr) ||
                            logData.TryGetValue("verifystyle", out vTypeStr))
                        {
                            if (int.TryParse(vTypeStr, out var v))
                                verifyMethod = v == 0 ? 3 : v;
                        }

                        isValid = true;
                    }
                }
                else
                {
                    // Formato posicional
                    if (parts.Length >= 2 && DateTime.TryParse(parts[1], out checkTime))
                    {
                        pin = parts[0].Trim();
                        checkType = parts.Length > 2 && int.TryParse(parts[2], out var s) ? s : 0;
                        verifyMethod = parts.Length > 3 && int.TryParse(parts[3], out var v) ? (v == 0 ? 3 : v) : 3;
                        isValid = true;
                    }
                }

                if (isValid && !string.IsNullOrEmpty(pin))
                {
                    bool isExternal = false;
                    if (pin.Length > 3)
                    {
                        var branchCode = pin.Substring(0, 3);
                        var externalBranch = (await _branchRepository.GetAllAsync()).FirstOrDefault(b => b.IsExternal && b.Code == branchCode);

                        if (externalBranch != null)
                        {
                            isExternal = true;
                            var actualEmployeeId = pin.Substring(3);

                            _logger.LogInformation("ADMS: Log detectado para sucursal externa {Code}. Transfiriendo empleado {Id} a {Host}",
                                branchCode, actualEmployeeId, externalBranch.ExternalHost);

                            await _logTransferService.TransferLogAsync(
                                externalBranch.ExternalHost!,
                                actualEmployeeId,
                                checkTime,
                                verifyMethod,
                                checkType);
                        }
                    }

                    if (!isExternal)
                    {
                        localLogs.Add(new AttendanceLogDto(pin, checkTime, verifyMethod, checkType));
                        processed++;

                        // Collect for calculation
                        uniqueCalculations.Add((pin, checkTime.Date));
                    }

                    // Rastrear el timestamp más reciente del batch (incluso si es externo, para no repetir descarga si aplica)
                    if (lastCheckTime == null || checkTime > lastCheckTime)
                        lastCheckTime = checkTime;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error línea: {Line}", line);
            }
        }

        if (localLogs.Count > 0)
        {
            var batchResult = await _mediator.Send(new RecordAttendanceBatchCommand(
                device.Id.Value.ToString(),
                localLogs));

            if (!batchResult.IsSuccess)
            {
                _logger.LogError("Error al procesar lote de asistencia ADMS: {Error}", batchResult.Error);
            }
        }

        if (processed > 0)
        {
            device.RecordSuccessfulDownload(processed);
            await _deviceRepository.UpdateAsync(device);

            if (lastCheckTime.HasValue)
                await _deviceRepository.UpdateLastAttLogTimestampAsync(SN, lastCheckTime.Value);

            await _unitOfWork.SaveChangesAsync();
            _logger.LogInformation("✅ {Count} registros de {SN}, stamp actualizado a {Stamp}",
                processed, SN, lastCheckTime);

            // Trigger attendance calculation for each affected employee and day
            foreach (var (pin, date) in uniqueCalculations)
            {
                try
                {
                    // Expand range by 1 day back to ensure night shifts are caught correctly
                    var processStartDate = date.AddDays(-1);
                    _jobScheduler.EnqueueAttendanceProcessing(processStartDate, date, pin);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error encolando cálculo de asistencia para empleado {Pin} en fecha {Date}", pin, date);
                }
            }
        }

        return processed;
    }

    private async Task ProcessDeviceData(string[] lines, string SN, string table)
    {
        foreach (var line in lines)
        {
            try
            {
                var parts = line.Split('\t');
                var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var part in parts)
                {
                    var kvp = part.Split('=', 2);
                    if (kvp.Length == 2) data[kvp[0].Trim()] = kvp[1].Trim();
                }

                if (!data.TryGetValue("PIN", out var pin)) continue;

                var employeeId = EmployeeId.From(pin);
                var employee = await _employeeRepository.GetByIdAsync(employeeId);
                if (employee == null)
                {
                    _logger.LogWarning("ADMS: Recibidos datos para PIN {Pin} pero el empleado no existe en BD. SN:{SN} Table:{Table}", pin, SN, table);
                    continue;
                }

                if (table.Equals("USERPIC", StringComparison.OrdinalIgnoreCase))
                {
                    // protocol can be FileName=... Content=...
                    if (data.TryGetValue("Content", out var base64))
                    {
                        _logger.LogInformation("📸 ADMS: Recibida foto de perfil para {Pin} ({SN})", pin, SN);
                        employee.UpdateBiometrics(photo: base64);
                    }
                }
                else if (table.Equals("BIODATA", StringComparison.OrdinalIgnoreCase))
                {
                    if (data.TryGetValue("Type", out var typeStr) && int.TryParse(typeStr, out var type) && data.TryGetValue("Content", out var content))
                    {
                        if (type == 0) // Fingerprint
                        {
                            int index = data.TryGetValue("Index", out var idxStr) && int.TryParse(idxStr, out var idx) ? idx : 0;
                            var fingerprints = new List<AttendanceSystem.Domain.Aggregates.EmployeeAggregate.EmployeeFingerprint>
                            {
                                new(index, content)
                            };
                            employee.UpdateBiometrics(fingerprints: fingerprints);
                            _logger.LogInformation("☝️ ADMS: Recibida huella {Index} para {Pin} ({SN})", index, pin, SN);
                        }
                        else if (type == 9) // Face
                        {
                            employee.UpdateBiometrics(faceTemplate: content);
                            _logger.LogInformation("👤 ADMS: Recibido rostro para {Pin} ({SN})", pin, SN);
                        }
                    }
                }
                else if (table.Equals("USER", StringComparison.OrdinalIgnoreCase) ||
                         table.Equals("USERINFO", StringComparison.OrdinalIgnoreCase))
                {
                    string? card = data.TryGetValue("Card", out var c) ? c : null;
                    string? pass = data.TryGetValue("Password", out var p) ? p : null;

                    employee.UpdateBiometrics(cardNumber: card, devicePassword: pass);
                    _logger.LogInformation("📝 ADMS: Recibida info de usuario para {Pin} ({SN})", pin, SN);
                }

                _employeeRepository.Update(employee);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando línea de datos ADMS: {Line}", line);
            }
        }
        await _unitOfWork.SaveChangesAsync();
    }
}
