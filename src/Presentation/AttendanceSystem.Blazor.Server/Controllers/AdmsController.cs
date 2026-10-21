using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Application.Features.Devices.Clock;
using AttendanceSystem.Application.Common;
using Microsoft.Extensions.DependencyInjection;
using AttendanceSystem.Infrastructure.Persistence;
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
using AttendanceSystem.Domain.Aggregates.ExternalLogAggregate;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;

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
    private readonly IExternalAttendanceLogRepository _externalLogRepository;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly ISystemAlertRepository? _alerts;
    private readonly IDeviceHeartbeatTracker? _heartbeatTracker;

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
        IAttendanceJobScheduler jobScheduler,
        IExternalAttendanceLogRepository externalLogRepository,
        ITimeNormalizer timeNormalizer,
        IServiceScopeFactory? scopeFactory = null,
        IAuthoritativeTimeZoneResolver? timeZoneResolver = null,
        ISystemAlertRepository? alerts = null,
        IDeviceHeartbeatTracker? heartbeatTracker = null)
    {
        _alerts = alerts;
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
        _externalLogRepository = externalLogRepository;
        _timeNormalizer = timeNormalizer;
        _scopeFactory = scopeFactory;
        _timeZoneResolver = timeZoneResolver ?? DefaultAuthoritativeTimeZoneResolver.Instance;
        _heartbeatTracker = heartbeatTracker;
    }

    private void TouchDeviceActivity(Device device, string sn)
    {
        var cleanSn = !string.IsNullOrWhiteSpace(sn) ? sn.Trim() : (device.HardwareInfo?.SerialNumber ?? "");
        _heartbeatTracker?.RecordActivity(cleanSn, device.Id.Value.ToString());
    }

    private async Task<Device?> ValidateAuthorizedDeviceAsync(string? sn)
    {
        if (string.IsNullOrWhiteSpace(sn))
        {
            _logger.LogWarning("Intento de acceso a endpoint ADMS sin número de serie (SN).");
            return null;
        }

        var cleanSn = sn.Trim();
        var device = await _deviceRepository.GetBySerialNumberAsync(cleanSn);
        if (device == null || !device.IsActive)
        {
            _logger.LogWarning("Intento de acceso ADMS rechazado para dispositivo no registrado o inactivo: {SN}", cleanSn);
            return null;
        }

        TouchDeviceActivity(device, cleanSn);
        return device;
    }

    private static string SanitizeCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return command;
        return Regex.Replace(
            command,
            @"(Passwd|Password|Pin|TMP|FP|PHOTO)=([^,\t\r\n]+)",
            "$1=[REDACTED]",
            RegexOptions.IgnoreCase);
    }

    // 1. GET /iclock/cdata — opciones de configuración o verificación de registro
    [HttpGet("cdata")]
    public async Task<IActionResult> CheckData(
        [FromQuery] string SN,
        [FromQuery] string? options = null,
        [FromQuery] string? PushOptionsFlag = null,
        [FromQuery] string? pushver = null)
    {
        var device = await ValidateAuthorizedDeviceAsync(SN);
        if (device == null)
        {
            return Unauthorized("Dispositivo no registrado o inactivo");
        }

        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";
        _logger.LogInformation("🔧 GET cdata {SN} (options: {Options}, pushver: {PushVer}, PushOptionsFlag: {PushOptionsFlag})",
            SN, options ?? "null", pushver ?? "null", PushOptionsFlag ?? "null");

        bool changed = false;
        if (!string.IsNullOrEmpty(pushver) && device.HardwareInfo?.PushVersion != pushver)
        {
            device.UpdateDeviceInfo((device.HardwareInfo ?? DeviceHardwareInfo.Empty) with { PushVersion = pushver });
            changed = true;
        }
        if (device.Status != DeviceStatus.Online)
        {
            device.MarkAsOnline();
            changed = true;
        }
        if (changed)
        {
            await _deviceRepository.UpdateAsync(device);
            await _unitOfWork.SaveChangesAsync();
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
        var device = await ValidateAuthorizedDeviceAsync(SN);
        if (device == null)
        {
            return Unauthorized("Dispositivo no registrado o inactivo");
        }

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        _logger.LogInformation("🤝 Registry {SN}", SN);

        // Parsear DeviceType del body
        var fields = body.Split(',')
            .Select(f => f.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

        fields.TryGetValue("DeviceType", out var deviceType); // "acc" o "att"

        device.SetDeviceType(deviceType ?? "att");
        device.MarkAsOnline();
        device.ResetLastDownloadAt();
        await _deviceRepository.UpdateAsync(device);
        await _unitOfWork.SaveChangesAsync();

        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";

        // Manual página 32: respuesta normal es SOLO RegistryCode
        var registryCode = Guid.NewGuid().ToString("N")[..10].ToUpper();
        return Content($"RegistryCode={registryCode}\n", "text/plain;charset=ISO-8859-1");
    }

    // 3. POST /iclock/push — aquí va la configuración completa (sección 7.5 del manual)
    [HttpPost("push")]
    public async Task<IActionResult> Push([FromQuery] string SN)
    {
        var device = await ValidateAuthorizedDeviceAsync(SN);
        if (device == null)
        {
            return Unauthorized("Dispositivo no registrado o inactivo");
        }

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

        // Obtener último timestamp de logs para este dispositivo (formato estándar: yyyy-MM-dd HH:mm:ss o 1970-01-01 00:00:00 para resincronización completa)
        var lastLog = await _deviceRepository.GetLastAttLogTimestampAsync(SN);
        var stamp = lastLog.HasValue
            ? lastLog.Value.ToString("yyyy-MM-dd HH:mm:ss")
            : "1970-01-01 00:00:00";

        // TransTables compatible con ambos modos (Transaction y User Transaction, Push 2.0 y 3.x)
        var transTable = isAccessMode ? "Transaction,User Transaction" : "Transaction";

        // Zona del propio reloj (su TimeZoneId o la del sistema), nunca la del sistema operativo del servidor: el reloj
        // fija su hora con el encabezado Date (UTC) más este desfase.
        var deviceTimeZone = await _timeZoneResolver.ResolveForDeviceAsync(device?.Id);
        var utcOffset = _timeNormalizer.ResolveTimeZone(deviceTimeZone).GetUtcOffset(DateTime.UtcNow);
        var tzOffsetHours = AdmsTimeZone.Format(utcOffset);
        if (utcOffset.Minutes != 0)
        {
            _logger.LogWarning("ADMS: la zona {TimeZone} del reloj {SN} tiene desfase {Offset}; ADMS sólo admite horas enteras.", deviceTimeZone, SN, utcOffset);
        }

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
            lastLog.HasValue ? lastLog.Value.ToString("yyyy-MM-dd HH:mm:ss") : "1970-01-01 00:00:00 (Full Sync)",
            isAccessMode ? "acc" : "att");

        return response;
    }

    // 4. POST /iclock/cdata — recibir datos (ATTLOG, rtlog, options, etc.)
    [HttpPost("cdata")]
    public async Task<IActionResult> ReceiveData([FromQuery] string SN, [FromQuery] string? table = null)
    {
        var device = await ValidateAuthorizedDeviceAsync(SN);
        if (device == null)
        {
            return Unauthorized("Dispositivo no registrado o inactivo");
        }

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
            var processedCount = await ProcessAttLogs(lines, SN);
            if (processedCount < 0)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "ERROR: Batch persistence failed\n");
            }
            return Content($"OK: {processedCount}", "text/plain");
        }

        if (table?.Equals("USER", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("USERINFO", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("USERPIC", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("BIOPHOTO", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("BIODATA", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("TEMPLATEV10", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("FINGERPRINT", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("FINGERTMP", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("TEMPLATE", StringComparison.OrdinalIgnoreCase) == true)
        {
            var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            await ProcessDeviceData(lines, SN, table);
            return Content("OK", "text/plain");
        }

        if (table?.Equals("options", StringComparison.OrdinalIgnoreCase) == true ||
            table?.Equals("tabledata", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogInformation("📋 [ADMS DIAGNÓSTICO] Opciones/Parámetros recibidos en cdata para SN: {SN}", SN);
            await ProcessDeviceOptions(body, SN);
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
        var device = await ValidateAuthorizedDeviceAsync(SN);
        if (device == null)
        {
            return Unauthorized("Dispositivo no registrado o inactivo");
        }

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var targetTable = tablename ?? table ?? "";
        _logger.LogInformation("📥 POST querydata SN:{SN} table:{Table} type:{Type}", SN, targetTable, type);

        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";

        if (targetTable.Equals("options", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("tabledata", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("📋 [ADMS DIAGNÓSTICO] Opciones/Parámetros recibidos en querydata para SN: {SN}", SN);
            await ProcessDeviceOptions(body, SN);
            return Content("OK", "text/plain");
        }

        if (targetTable.Equals("ATTLOG", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("Transaction", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("User Transaction", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("USERTRANSACTION", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("rtlog", StringComparison.OrdinalIgnoreCase))
        {
            var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var processedCount = await ProcessAttLogs(lines, SN);
            if (processedCount < 0)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "ERROR: Batch persistence failed\n");
            }
            // Protocolo Push de ZKTeco: en /iclock/querydata el dispositivo requiere estrictamente la respuesta "OK"
            // para confirmar el paquete recibido (packidx) y proceder a enviar los siguientes paquetes (hasta packcnt).
            return Content("OK", "text/plain");
        }

        if (targetTable.Equals("USER", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("USERINFO", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("USERPIC", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("BIOPHOTO", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("BIODATA", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("TEMPLATEV10", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("FINGERPRINT", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("FINGERTMP", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("TEMPLATE", StringComparison.OrdinalIgnoreCase))
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
        var device = await ValidateAuthorizedDeviceAsync(SN);
        if (device == null)
        {
            return Unauthorized("Dispositivo no registrado o inactivo");
        }

        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";
        _logger.LogInformation("🔔 getrequest {SN}", SN);

        if (device.Status != DeviceStatus.Online)
        {
            device.MarkAsOnline();
            await _deviceRepository.UpdateAsync(device);
            await _unitOfWork.SaveChangesAsync();
        }

        if (_admsCommandService.HasPendingCommands(SN))
        {
            var (cmdId, command, logId) = _admsCommandService.GetNextCommand(SN);
            if (!string.IsNullOrEmpty(command) && !string.IsNullOrEmpty(cmdId))
            {
                _logger.LogInformation("📤 Comando → {SN}: {Command}", SN, SanitizeCommand(command));
                return Content($"C:{cmdId}:{command}\n", "text/plain;charset=ISO-8859-1");
            }
        }

        return Content("OK", "text/plain;charset=ISO-8859-1");
    }

    // Diagnostic endpoint to check pending commands
    [Authorize(Roles = "Administrador")]
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
    public async Task<IActionResult> Ping([FromQuery] string? SN = null)
    {
        if (!string.IsNullOrWhiteSpace(SN))
        {
            _ = await ValidateAuthorizedDeviceAsync(SN);
        }
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
            if (body.Contains("="))
            {
                // El encabezado form-encoded termina antes de la lista de opciones de INFO.
                var header = body.Split(new[] { '\r', '\n' }, 2)[0];
                var parsed = System.Web.HttpUtility.ParseQueryString(header);
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

        var device = await ValidateAuthorizedDeviceAsync(sn);
        if (device == null)
        {
            return Unauthorized("Dispositivo no registrado o inactivo");
        }

        _logger.LogInformation("📬 devicecmd SN:{SN} ID:{ID} Return:{Return} CMD:{CMD}", sn, id, ret, cmd);

        // Si el body contiene opciones o parámetros del dispositivo devueltos por INFO o DATA QUERY
        var isInfoResponse = cmd.Equals("INFO", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("FirmwareVersion", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("FWVersion", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("~SerialNumber", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("UserCount", StringComparison.OrdinalIgnoreCase) ||
             body.Contains("~UserCount", StringComparison.OrdinalIgnoreCase);
        if (isInfoResponse && int.TryParse(ret, out var infoReturnCode) && infoReturnCode >= 0)
        {
            _logger.LogInformation("📋 [ADMS] Información técnica recibida en devicecmd para SN: {SN}", sn);
            await ProcessDeviceOptions(body, sn, id);
        }

        // Si el body contiene registros de asistencia (en formato TSV o key=value) devueltos por DATA QUERY
        if (!isInfoResponse && !string.IsNullOrEmpty(sn) && (body.Contains("\t") || body.Contains("pin=") || body.Contains("PIN=")))
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
                var returnCode = int.TryParse(ret, out var r) ? r : -1;
                var logId = await _admsCommandService.AcknowledgeCommandExecutionAsync(sn, id, returnCode);
                if (!logId.HasValue)
                {
                    logId = _admsCommandService.GetAndRemovePendingExecution(sn, id);
                }

                if (logId.HasValue)
                {
                    var log = await _downloadLogRepository.GetByIdAsync(new AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLogId(logId.Value));
                    if (log != null)
                    {
                        if (returnCode >= 0 || returnCode == -2)
                        {
                            // Return >= 0 o -2 = éxito o sin datos nuevos en el rango
                            log.MarkAsSuccessful(0, 0);

                            // Actualizar LastDownloadAt del dispositivo usando el ToDate solicitado
                            if (log.ToDate.HasValue)
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
        bool batchHasFailures = false;
        DateTime? lastCheckTime = null;
        var localLogs = new List<AttendanceLogDto>();
        var incidents = new List<AdmsIngestionIncident>();

        // F06-P03 / H22: Precargar sucursales externas una sola vez para todo el lote
        var externalBranches = (await _branchRepository.GetAllAsync())
            .Where(b => b.IsExternal)
            .ToDictionary(b => b.Code, StringComparer.OrdinalIgnoreCase);

        var pendingExternalLogs = new List<ExternalAttendanceLog>();

        // La hora que marca el reloj se interpreta en SU zona (la del dispositivo o, si no tiene, la del sistema).
        var deviceTimeZone = await _timeZoneResolver.ResolveForDeviceAsync(device.Id);

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
                            var key = kvp[0].Trim();
                            var spaceIdx = key.LastIndexOf(' ');
                            if (spaceIdx >= 0)
                            {
                                key = key.Substring(spaceIdx + 1).Trim();
                            }
                            logData[key] = kvp[1].Trim();
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

                    if (!string.IsNullOrWhiteSpace(pin) && !string.IsNullOrWhiteSpace(timeStr) && TryParseZkDateTime(timeStr, out checkTime))
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
                    if (parts.Length >= 2 && TryParseZkDateTime(parts[1], out checkTime))
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
                        if (externalBranches.TryGetValue(branchCode, out var externalBranch))
                        {
                            isExternal = true;
                            var actualEmployeeId = pin.Substring(3);

                            _logger.LogInformation("ADMS: Log detectado para sucursal externa {Code}. Almacenando localmente (transferencia diferida) para empleado {Id}",
                                branchCode, actualEmployeeId);

                            var utcCheckTime = _timeNormalizer.ToUtc(checkTime, deviceTimeZone);

                            var localExtLog = ExternalAttendanceLog.Create(
                                branchCode,
                                actualEmployeeId,
                                utcCheckTime,
                                verifyMethod,
                                checkType,
                                SN);

                            pendingExternalLogs.Add(localExtLog);
                            processed++;
                        }
                    }

                    if (!isExternal)
                    {
                        var utcCheckTime = _timeNormalizer.ToUtc(checkTime, deviceTimeZone);
                        var workDate = _timeNormalizer.ToWorkDate(utcCheckTime, deviceTimeZone);

                        localLogs.Add(new AttendanceLogDto(
                            pin,
                            utcCheckTime,
                            verifyMethod,
                            checkType,
                            OriginalCheckTime: checkTime,
                            WorkDate: workDate,
                            TimeZoneId: deviceTimeZone,
                            Source: "ADMS"));
                        processed++;
                    }

                    // Rastrear el timestamp más reciente del batch (incluso si es externo, para no repetir descarga si aplica)
                    if (lastCheckTime == null || checkTime > lastCheckTime)
                        lastCheckTime = checkTime;
                }
                else
                {
                    batchHasFailures = true;
                    incidents.Add(AdmsIngestionIncident.Create(
                        SN,
                        line,
                        "Línea de asistencia malformada o campos obligatorios no válidos"));
                }
            }
            catch (Exception ex)
            {
                batchHasFailures = true;
                _logger.LogError(ex, "Error línea: {Line}", line);
                incidents.Add(AdmsIngestionIncident.Create(
                    SN,
                    line,
                    $"Excepción parseando línea: {ex.Message}"));
            }
        }

        if (pendingExternalLogs.Count > 0)
        {
            try
            {
                foreach (var extLog in pendingExternalLogs)
                {
                    await _externalLogRepository.AddAsync(extLog);
                }
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation("ADMS: Se almacenaron localmente {Count} registros externos pendientes de sincronización cloud", pendingExternalLogs.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al persistir registros externos ADMS locales");
                batchHasFailures = true;
                incidents.Add(AdmsIngestionIncident.Create(
                    SN,
                    $"ExternalBatchCount={pendingExternalLogs.Count}",
                    $"Error al guardar logs externos: {ex.Message}"));
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
                batchHasFailures = true;
                incidents.Add(AdmsIngestionIncident.Create(
                    SN,
                    $"BatchCount={localLogs.Count}",
                    $"Error al guardar lote en base de datos: {batchResult.Error}"));
                processed = 0;
                return -1; // F01-P02 / H11: Indicar fallo explícito para no confirmar al terminal ni avanzar cursor
            }
        }

        if (incidents.Count > 0 && _scopeFactory != null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
                await dbContext.AdmsIngestionIncidents.AddRangeAsync(incidents);
                await dbContext.SaveChangesAsync();
                _logger.LogWarning("⚠️ ADMS: Se registraron {Count} incidentes de ingesta para SN: {SN}", incidents.Count, SN);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar incidentes de ingesta ADMS para SN: {SN}", SN);
            }
        }

        if (processed > 0)
        {
            device.RecordSuccessfulDownload(processed);
            await _deviceRepository.UpdateAsync(device);

            // Un marcaje posterior a la hora actual de la zona del reloj indica que su hora se adelantó: se le reenvían
            // su zona y su hora (se aplican en su siguiente consulta de comandos) y queda una alerta.
            var expectedNow = _timeNormalizer.ToCivilTime(DateTime.UtcNow, deviceTimeZone);
            if (lastCheckTime.HasValue && lastCheckTime.Value - expectedNow > DeviceClockService.DriftTolerance)
            {
                var utcOffset = _timeNormalizer.ResolveTimeZone(deviceTimeZone).GetUtcOffset(DateTime.UtcNow);
                foreach (var clockCommand in AdmsTimeZone.ClockCommands(expectedNow, utcOffset))
                {
                    _admsCommandService.EnqueueCommand(SN, clockCommand);
                }
                AttendanceTelemetry.DeviceClockCorrections.Add(1, new KeyValuePair<string, object?>("outcome", "queued"));
                _logger.LogWarning("ALERTA: el reloj ADMS {SN} envió un marcaje de {Reported:yyyy-MM-dd HH:mm:ss} cuando en su zona {TimeZone} eran las {Expected:yyyy-MM-dd HH:mm:ss}. Se encoló su resincronización.",
                    SN, lastCheckTime.Value, deviceTimeZone, expectedNow);
                if (_alerts != null)
                {
                    await _alerts.AddAsync(SystemAlert.Create(AlertType.DeviceClockDrift, device.Id.Value,
                        $"El reloj {device.Name} envió un marcaje de {lastCheckTime.Value:dd/MM/yyyy HH:mm} cuando en su zona eran las {expectedNow:dd/MM/yyyy HH:mm}; se reenviaron su zona y su hora."));
                }

                // El cursor no avanza más allá de la hora actual del reloj: si no, tras corregirlo se saltarían los
                // marcajes legítimos con hora anterior a la que reportó adelantado.
                lastCheckTime = expectedNow;
            }

            if (!batchHasFailures && lastCheckTime.HasValue)
            {
                await _deviceRepository.UpdateLastAttLogTimestampAsync(SN, lastCheckTime.Value);
            }
            else if (batchHasFailures)
            {
                _logger.LogWarning("⚠️ ADMS: Cursor temporal no avanzado para {SN} debido a fallas/rechazos en el lote", SN);
            }

            await _unitOfWork.SaveChangesAsync();
            _logger.LogInformation("✅ {Count} registros de {SN}, stamp actualizado a {Stamp} (fallas={HasFailures})",
                processed, SN, (!batchHasFailures && lastCheckTime.HasValue ? lastCheckTime.ToString() : "NO_AVANZADO"), batchHasFailures);

            // F04-01 / F04-05: la consolidación ya no se encola aquí. Cada marcaje insertado emite AttendanceRecordedEvent
            // y el outbox registra una solicitud durable por empleado, que el consumidor agrupa y procesa.
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
                    if (kvp.Length == 2)
                    {
                        var key = kvp[0].Trim();
                        // El primer token suele traer el prefijo de la tabla, ej: "biodata pin=187" o "user uid=1" o "biophoto pin=1056"
                        var spaceIndex = key.LastIndexOf(' ');
                        if (spaceIndex >= 0)
                        {
                            key = key.Substring(spaceIndex + 1).Trim();
                        }
                        data[key] = kvp[1].Trim();
                    }
                }

                if (!data.TryGetValue("PIN", out var pin) || string.IsNullOrWhiteSpace(pin)) continue;

                var employeeId = EmployeeId.From(pin);
                var employee = await _employeeRepository.GetByIdAsync(employeeId);
                if (employee == null)
                {
                    _logger.LogWarning("ADMS: Recibidos datos para PIN {Pin} pero el empleado no existe en BD. SN:{SN} Table:{Table}", pin, SN, table);
                    continue;
                }

                if (table.Equals("USERPIC", StringComparison.OrdinalIgnoreCase) ||
                    table.Equals("BIOPHOTO", StringComparison.OrdinalIgnoreCase))
                {
                    // protocol can be FileName=... Content=... or tmp=...
                    string? photoContent = null;
                    if (data.TryGetValue("Content", out var c) && !string.IsNullOrWhiteSpace(c))
                        photoContent = c;
                    else if (data.TryGetValue("tmp", out var t) && !string.IsNullOrWhiteSpace(t))
                        photoContent = t;

                    if (!string.IsNullOrWhiteSpace(photoContent))
                    {
                        _logger.LogInformation("📸 ADMS: Recibida foto ({Table}) para {Pin} ({SN})", table, pin, SN);
                        employee.UpdateBiometrics(photo: photoContent);
                    }
                }
                else if (table.Equals("BIODATA", StringComparison.OrdinalIgnoreCase))
                {
                    int type = 0;
                    if (data.TryGetValue("Type", out var typeStr) && int.TryParse(typeStr, out var parsedType))
                    {
                        type = parsedType;
                    }

                    // En ZK Push, la plantilla biométrica puede venir en tmp=, Content= o template=
                    string? templateContent = null;
                    if (data.TryGetValue("tmp", out var tmp) && !string.IsNullOrWhiteSpace(tmp))
                        templateContent = tmp;
                    else if (data.TryGetValue("Content", out var c) && !string.IsNullOrWhiteSpace(c))
                        templateContent = c;
                    else if (data.TryGetValue("template", out var tmpl) && !string.IsNullOrWhiteSpace(tmpl))
                        templateContent = tmpl;

                    if (!string.IsNullOrWhiteSpace(templateContent))
                    {
                        if (type == 0) // Fingerprint
                        {
                            int index = data.TryGetValue("Index", out var idxStr) && int.TryParse(idxStr, out var idx) ? idx : 0;
                            var fingerprints = new List<AttendanceSystem.Domain.Aggregates.EmployeeAggregate.EmployeeFingerprint>
                            {
                                new(index, templateContent)
                            };
                            employee.UpdateBiometrics(fingerprints: fingerprints);
                            _logger.LogInformation("☝️ ADMS: Recibida huella {Index} para {Pin} ({SN})", index, pin, SN);
                        }
                        else if (type == 9) // Face
                        {
                            employee.UpdateBiometrics(faceTemplate: templateContent);
                            _logger.LogInformation("👤 ADMS: Recibido rostro para {Pin} ({SN})", pin, SN);
                        }
                    }
                }
                else if (table.Equals("TEMPLATEV10", StringComparison.OrdinalIgnoreCase) ||
                         table.Equals("FINGERPRINT", StringComparison.OrdinalIgnoreCase) ||
                         table.Equals("FINGERTMP", StringComparison.OrdinalIgnoreCase) ||
                         table.Equals("TEMPLATE", StringComparison.OrdinalIgnoreCase))
                {
                    // templatev10 size=892	uid=1	pin=4	fingerid=6	valid=1	template=...
                    int index = 0;
                    if (data.TryGetValue("fingerid", out var fIdStr) && int.TryParse(fIdStr, out var fId))
                        index = fId;
                    else if (data.TryGetValue("finger_id", out var fIdStr2) && int.TryParse(fIdStr2, out var fId2))
                        index = fId2;
                    else if (data.TryGetValue("Index", out var idxStr) && int.TryParse(idxStr, out var idx))
                        index = idx;

                    string? templateContent = null;
                    if (data.TryGetValue("template", out var tmpl) && !string.IsNullOrWhiteSpace(tmpl))
                        templateContent = tmpl;
                    else if (data.TryGetValue("tmp", out var tmp) && !string.IsNullOrWhiteSpace(tmp))
                        templateContent = tmp;
                    else if (data.TryGetValue("Content", out var c) && !string.IsNullOrWhiteSpace(c))
                        templateContent = c;

                    if (!string.IsNullOrWhiteSpace(templateContent))
                    {
                        var fingerprints = new List<AttendanceSystem.Domain.Aggregates.EmployeeAggregate.EmployeeFingerprint>
                        {
                            new(index, templateContent)
                        };
                        employee.UpdateBiometrics(fingerprints: fingerprints);
                        _logger.LogInformation("☝️ ADMS: Recibida huella v10 dedo {Index} para {Pin} ({SN})", index, pin, SN);
                    }
                }
                else if (table.Equals("USER", StringComparison.OrdinalIgnoreCase) ||
                         table.Equals("USERINFO", StringComparison.OrdinalIgnoreCase))
                {
                    string? card = null;
                    if (data.TryGetValue("cardno", out var cNo) && !string.IsNullOrWhiteSpace(cNo)) card = cNo;
                    else if (data.TryGetValue("card", out var c) && !string.IsNullOrWhiteSpace(c)) card = c;

                    string? pass = null;
                    if (data.TryGetValue("Password", out var p) && !string.IsNullOrWhiteSpace(p)) pass = p;
                    else if (data.TryGetValue("Passwd", out var pw) && !string.IsNullOrWhiteSpace(pw)) pass = pw;

                    employee.UpdateBiometrics(cardNumber: card, devicePassword: pass);

                    // Privilegio del dispositivo (privilege/pri: 14 superadmin, 2 admin, 0 usuario)
                    if ((data.TryGetValue("privilege", out var privStr) || data.TryGetValue("pri", out privStr)) && int.TryParse(privStr, out var priv))
                    {
                        var devPrivilege = DevicePrivilegeMapper.MapFromProtocolValue(DeviceBrand.ZKTeco, DeviceDownloadMethod.Adms, priv);

                        if (employee.DevicePrivilege != devPrivilege)
                        {
                            employee.UpdateDevicePrivilege(devPrivilege);
                        }
                    }

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

    /// <summary>
    /// Intenta parsear una fecha/hora proveniente de dispositivos ZKTeco, soportando
    /// texto estándar (yyyy-MM-dd HH:mm:ss), formato comprimido time_second (base 2000) o timestamp Unix.
    /// </summary>
    private static bool TryParseZkDateTime(string? timeStr, out DateTime checkTime)
    {
        checkTime = default;
        if (string.IsNullOrWhiteSpace(timeStr))
            return false;

        timeStr = timeStr.Trim();

        // 1. Intento estándar DateTime.TryParse
        if (DateTime.TryParse(timeStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out checkTime) ||
            DateTime.TryParse(timeStr, out checkTime))
        {
            return true;
        }

        // 2. Si viene como número (time_second de ZKTeco en segundos comprimidos o Unix timestamp)
        if (long.TryParse(timeStr, out var timeSec) && timeSec > 0)
        {
            // Decodificación algoritmo ZKTeco (segundos base año 2000 con meses de 31 días)
            try
            {
                long temp = timeSec;
                int second = (int)(temp % 60);
                temp /= 60;
                int minute = (int)(temp % 60);
                temp /= 60;
                int hour = (int)(temp % 24);
                temp /= 24;
                int day = (int)(temp % 31) + 1;
                temp /= 31;
                int month = (int)(temp % 12) + 1;
                int year = (int)(temp / 12) + 2000;

                if (year >= 2000 && year <= 2099 && month >= 1 && month <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
                {
                    checkTime = new DateTime(year, month, day, hour, minute, second);
                    return true;
                }
            }
            catch
            {
                // Fallback si falla el cálculo
            }

            // Fallback: Unix epoch (1970)
            try
            {
                if (timeSec >= 946684800 && timeSec <= 4102444800)
                {
                    checkTime = DateTimeOffset.FromUnixTimeSeconds(timeSec).LocalDateTime;
                    return true;
                }
            }
            catch
            {
                // Ignorar
            }
        }

        return false;
    }

    private async Task ProcessDeviceOptions(string body, string SN, string? commandId = null)
    {
        if (string.IsNullOrWhiteSpace(SN) || string.IsNullOrWhiteSpace(body)) return;

        try
        {
            var device = await _deviceRepository.GetBySerialNumberAsync(SN);
            if (device == null) return;

            var lines = body.Split(new[] { '\r', '\n', ',', '\t', '&' }, StringSplitOptions.RemoveEmptyEntries);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var val = parts[1].Trim();
                    if (!dict.ContainsKey(key))
                    {
                        dict[key] = val;
                    }
                    var cleanKey = key.TrimStart('~');
                    if (!dict.ContainsKey(cleanKey))
                    {
                        dict[cleanKey] = val;
                    }
                }
            }

            var debugParams = dict
                .Where(kv => !kv.Key.Contains("pass", StringComparison.OrdinalIgnoreCase))
                .Select(kv => $"{kv.Key}={kv.Value}");
            _logger.LogInformation("📋 [ADMS PARÁMETROS COMPLETOS {SN}]:\n{Params}", SN, string.Join(", ", debugParams));

            string? GetVal(params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (dict.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                        return v;
                    var altKey = k.StartsWith('~') ? k[1..] : "~" + k;
                    if (dict.TryGetValue(altKey, out var altV) && !string.IsNullOrWhiteSpace(altV))
                        return altV;
                }
                return null;
            }

            int? GetInt(params string[] keys)
            {
                var v = GetVal(keys);
                if (int.TryParse(v, out var n) && n >= 0) return n;
                return null;
            }

            var fw = GetVal("FirmwareVersion", "~FirmwareVersion", "FWVersion");
            var platform = GetVal("Platform", "~Platform");
            var pushVer = GetVal("PushVersion", "PushProtVer", "pushver", "~PushVersion");
            var sdkVer = GetVal("SDKVersion", "~SDKVersion");
            var mac = GetVal("MAC", "~MAC", "MacAddress");
            var devType = GetVal("DeviceType", "~DeviceType");

            var userCount = GetInt("UserCount", "~UserCount");
            var faceCount = GetInt("FaceCount", "~FaceCount");

            // Detección de soporte de huellas digitales en hardware:
            // 1. Firmwares ZKTeco con "NF" (Non-Fingerprint), p. ej. "ZAM170-NF-Ver1.1.20".
            // 2. Parámetro FingerFunOn / ~FingerFunOn == 0.
            // 3. ZKFPVersion == "0" o ausente en modelos NF.
            var isNoFingerprintDevice =
                (fw?.Contains("-NF", StringComparison.OrdinalIgnoreCase) == true) ||
                (platform?.Contains("-NF", StringComparison.OrdinalIgnoreCase) == true) ||
                (GetVal("FingerFunOn", "~FingerFunOn") == "0") ||
                (GetVal("ZKFPVersion", "~ZKFPVersion") == "0" && fw?.Contains("-NF", StringComparison.OrdinalIgnoreCase) == true);

            int? fpCount = null;
            int? fpCap = null;
            if (isNoFingerprintDevice)
            {
                fpCount = 0;
                fpCap = 0;
            }
            else
            {
                fpCount = GetInt("FPCount", "FingerCount", "~FingerCount");
                var rawFpCap = GetInt("~MaxFingerCount", "MaxFinger", "MaxFingerCapacity");
                if (rawFpCap.HasValue && rawFpCap.Value > 0)
                {
                    fpCap = (rawFpCap.Value <= 100) ? rawFpCap.Value * 100 : rawFpCap.Value;
                }
            }

            // Conteo de registros (Logs):
            // En modo acceso (acc) o ADMS push, AttLogCount reporta registros nuevos/pendientes (frecuentemente 0),
            // mientras TransactionCount / RecordCount / LogCount / AttCount reporta el total histórico de checadas en memoria.
            var transCount = GetInt("TransactionCount", "RecordCount", "LogCount", "AttCount", "TotalLogCount", "TotalRecordCount", "TotalTransactionCount");
            var attLogCount = GetInt("AttLogCount");
            int? attCount = (transCount.HasValue && attLogCount.HasValue)
                ? Math.Max(transCount.Value, attLogCount.Value)
                : (transCount ?? attLogCount);

            // Escalamiento de capacidades según estándares ZKTeco (valores <= 100 vienen en factores de escala):
            var rawUserCap = GetInt("~MaxUserCount", "MaxUser", "MaxUserCapacity");
            int? userCap = null;
            if (rawUserCap.HasValue && rawUserCap.Value > 0)
            {
                userCap = (rawUserCap.Value <= 100) ? rawUserCap.Value * 100 : rawUserCap.Value;
                if (userCount.HasValue && userCap.Value < userCount.Value)
                {
                    userCap = Math.Max(userCount.Value > 3000 ? 10000 : 3000, userCount.Value);
                }
            }

            var rawFaceCap = GetInt("~MaxFaceCount", "MaxFace", "MaxFaceCapacity");
            int? faceCap = null;
            if (rawFaceCap.HasValue && rawFaceCap.Value > 0)
            {
                faceCap = (rawFaceCap.Value <= 100) ? rawFaceCap.Value * 100 : rawFaceCap.Value;
            }

            var rawAttCap = GetInt("~MaxAttLogCount", "MaxAttLog", "MaxAttLogCapacity");
            int? attCap = null;
            if (rawAttCap.HasValue && rawAttCap.Value > 0)
            {
                attCap = (rawAttCap.Value <= 100) ? rawAttCap.Value * 10000 : rawAttCap.Value;
                if (attCount.HasValue && attCap.Value < attCount.Value)
                {
                    attCap = Math.Max(attCount.Value > 50000 ? 100000 : 50000, attCount.Value);
                }
            }

            var reportedSn = GetVal("~SerialNumber", "SerialNumber");
            if (reportedSn != null && !reportedSn.Equals(SN, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("ADMS: información técnica descartada por número de serie distinto para {SN}", SN);
                return;
            }

            // Un ACK, opciones desconocidas o contadores inválidos no constituyen datos técnicos nuevos.
            if (fw == null && platform == null && pushVer == null && sdkVer == null && mac == null &&
                userCount == null && fpCount == null && faceCount == null && attCount == null &&
                userCap == null && fpCap == null && faceCap == null && attCap == null)
            {
                return;
            }

            var currentHw = device.HardwareInfo ?? DeviceHardwareInfo.Empty;

            var finalUserCap = userCap ?? (currentHw.UserCapacity.HasValue && currentHw.UserCapacity.Value <= 100 && currentHw.UserCapacity.Value > 0 ? currentHw.UserCapacity.Value * 100 : currentHw.UserCapacity);
            var finalAttCap = attCap ?? (currentHw.AttendanceRecordCapacity.HasValue && currentHw.AttendanceRecordCapacity.Value <= 100 && currentHw.AttendanceRecordCapacity.Value > 0 ? currentHw.AttendanceRecordCapacity.Value * 10000 : currentHw.AttendanceRecordCapacity);

            var updatedHw = currentHw with
            {
                FirmwareVersion = fw ?? currentHw.FirmwareVersion,
                Platform = platform ?? currentHw.Platform,
                PushVersion = pushVer ?? currentHw.PushVersion,
                SdkVersion = sdkVer ?? currentHw.SdkVersion,
                MacAddress = mac ?? currentHw.MacAddress,
                UserCount = userCount ?? currentHw.UserCount,
                FingerprintCount = isNoFingerprintDevice ? 0 : (fpCount ?? currentHw.FingerprintCount),
                FaceCount = faceCount ?? currentHw.FaceCount,
                AttendanceRecordCount = attCount ?? currentHw.AttendanceRecordCount,
                UserCapacity = finalUserCap,
                FingerprintCapacity = isNoFingerprintDevice ? 0 : (fpCap ?? currentHw.FingerprintCapacity),
                FaceCapacity = faceCap ?? currentHw.FaceCapacity,
                AttendanceRecordCapacity = finalAttCap
            };

            device.UpdateDeviceInfo(updatedHw);

            if (!string.IsNullOrWhiteSpace(devType))
            {
                device.SetDeviceType(devType);
            }

            device.MarkAsOnline();
            await _deviceRepository.UpdateAsync(device);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("✅ [ADMS] Información del dispositivo {SN} actualizada desde opciones: FW={FW}, PushVer={PushVer}, SDKVer={SDKVer}, Platform={Platform}, MAC={MAC}, Users={Users}/{UserCap}",
                SN, updatedHw.FirmwareVersion, updatedHw.PushVersion, updatedHw.SdkVersion, updatedHw.Platform, updatedHw.MacAddress, updatedHw.UserCount, updatedHw.UserCapacity);

            var deviceInfoDto = new AttendanceSystem.Application.DTOs.DeviceInfoDto(
                SN,
                device.Name,
                updatedHw.FirmwareVersion ?? "Desconocido",
                updatedHw.Platform ?? "ZKTeco",
                updatedHw.UserCount ?? 0,
                updatedHw.FingerprintCount ?? 0,
                updatedHw.FaceCount ?? 0,
                updatedHw.AttendanceRecordCount ?? 0,
                updatedHw.UserCapacity ?? 0,
                updatedHw.FingerprintCapacity ?? 0,
                updatedHw.FaceCapacity ?? 0,
                updatedHw.AttendanceRecordCapacity ?? 0,
                updatedHw.PushVersion,
                updatedHw.SdkVersion,
                updatedHw.MacAddress
            );

            _admsCommandService.NotifyDeviceInfoReceived(SN, deviceInfoDto, commandId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al procesar opciones de dispositivo ADMS para SN {SN}", SN);
        }
    }
}
