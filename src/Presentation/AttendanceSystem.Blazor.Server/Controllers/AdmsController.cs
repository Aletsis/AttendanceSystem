using Microsoft.AspNetCore.Mvc;
using MediatR;
using AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance; 
using AttendanceSystem.Domain.Enumerations;
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

    public AdmsController(
        ILogger<AdmsController> logger, 
        IMediator mediator,
        IDeviceRepository deviceRepository,
        IUnitOfWork unitOfWork,
        IAdmsCommandService admsCommandService,
        IDownloadLogRepository downloadLogRepository)
    {
        _logger = logger;
        _mediator = mediator;
        _deviceRepository = deviceRepository;
        _unitOfWork = unitOfWork;
        _admsCommandService = admsCommandService;
        _downloadLogRepository = downloadLogRepository;
    }

    // 1. GET /iclock/cdata — solo dice si está registrado o no
    [HttpGet("cdata")]
    public IActionResult CheckData([FromQuery] string SN, [FromQuery] string? options = null)
    {
        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";
        _logger.LogInformation("🔧 GET cdata {SN}", SN);

        // El manual dice: si el dispositivo NO está registrado, responder solo "OK"
        // Si YA está registrado, responder con registry=ok + config
        // Por simplicidad, siempre respondemos "OK" para forzar el registro
        return Content("OK", "text/plain");
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
            device.SetDeviceType(deviceType ?? "acc");
            await _deviceRepository.UpdateAsync(device);
            await _unitOfWork.SaveChangesAsync();
        }

        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";

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

        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";

        var device = await _deviceRepository.GetBySerialNumberAsync(SN);
        var isAccessMode = device?.DeviceType == "acc";

        // Obtener último timestamp de logs para este dispositivo
        var lastLog = await _deviceRepository.GetLastAttLogTimestampAsync(SN);
        // En referencias (ZKTecoADMS repo) se evalúa como la cadena de fecha literal ISO yyyy-MM-ddTHH:mm:ss:
        var stamp = lastLog.HasValue
            ? lastLog.Value.ToString("yyyy-MM-ddTHH:mm:ss")
            : "0";

        // TransTables diferente según el modo
        var transTable = isAccessMode ? "Transaction" : "User Transaction";

        // Manual sección 7.5: esta es la respuesta correcta al /push
        var sessionId = Guid.NewGuid().ToString("N").ToUpper();
        var response = $"ServerVersion=3.1.2\n" +
                       $"ServerName=ADMS\n" +
                       $"PushVersion=3.1.2\n" +
                       $"ErrorDelay=60\n" +
                       $"RequestDelay=2\n" +
                       $"TransTimes=00:00;14:00\n" +
                       $"TransInterval=1\n" +
                       $"TransTables={transTable}\n" +
                       $"Realtime=1\n" +
                       $"SessionID={sessionId}\n" +
                       $"TimeoutSec=10\n" +
                       $"ATTLOGStamp={stamp}\n" +    // ← aquí va el stamp, no en registry
                       $"OPERLOGStamp=9999\n" +
                       $"ATTPHOTOStamp=9999\n";

        _logger.LogInformation("📋 Push {SN} stamp={Stamp} ({StampReadable}) mode={Mode}",
            SN, stamp,
            lastLog.HasValue ? lastLog.Value.ToString("yyyy-MM-dd HH:mm:ss") : "ninguno",
            isAccessMode ? "acc" : "att");

        return Content(response, "text/plain;charset=ISO-8859-1");
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

        // rtstate, options, tabledata, etc. — responder OK
        return Content("OK", "text/plain");
    }

    // 5. GET /iclock/getrequest — heartbeat y comandos (sin cambios)
    [HttpGet("getrequest")]
    public IActionResult GetRequest([FromQuery] string SN)
    {
        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";
        _logger.LogInformation("� getrequest {SN}", SN);

        if (_admsCommandService.HasPendingCommands(SN))
        {
            var (command, logId) = _admsCommandService.GetNextCommand(SN);
            if (!string.IsNullOrEmpty(command))
            {
                var cmdId = DateTime.UtcNow.Ticks.ToString();
                if (logId.HasValue)
                    _admsCommandService.RegisterPendingExecution(SN, cmdId, logId.Value);

                _logger.LogInformation("📤 Comando → {SN}: {Command}", SN, command);
                return Content($"C:{cmdId}:{command}\n", "text/plain");
            }
        }

        return Content("OK", "text/plain");
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
        Response.Headers["Date"] = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT";

        // El reloj puede mandar SN, ID, Return en query string O en el body
        string sn = SN ?? "";
        string id = "", ret = "", cmd = "";

        try 
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            
            // Parsear — pueden venir en body como form-encoded
            var parsed = System.Web.HttpUtility.ParseQueryString(body);
            id  = Request.Query["ID"].FirstOrDefault()  ?? parsed["ID"]     ?? "";
            ret = Request.Query["Return"].FirstOrDefault() ?? parsed["Return"] ?? "";
            cmd = Request.Query["CMD"].FirstOrDefault() ?? parsed["CMD"]    ?? "";
            
            if (string.IsNullOrEmpty(sn))
                sn = parsed["SN"] ?? "";
        }
        catch(Exception ex)
        {
            _logger.LogWarning(ex, "Error leyendo request en DeviceCmd");
        }

        _logger.LogInformation("📬 devicecmd SN:{SN} ID:{ID} Return:{Return} CMD:{CMD}", sn, id, ret, cmd);
        
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
                        
                        if (returnCode >= 0)
                        {
                             // Return >= 0 = éxito según el manual (Appendix 1)
                             log.MarkAsSuccessful(0, 0); 
                             _logger.LogInformation("✅ ADMS: Descarga completada SN:{SerialNumber} CMD:{CommandId}", sn, id);
                        }
                        else if (returnCode == -2)
                        {
                             // Return -2 = No hay datos en el rango
                             log.MarkAsSuccessful(0, 0);
                             _logger.LogInformation("✅ ADMS: Descarga completada (sin datos) SN:{SerialNumber} CMD:{CommandId}", sn, id);
                        }
                        else
                        {
                             // Fallo reportado por el dispositivo
                             log.MarkAsFailed($"Dispositivo retornó error: {ret}");
                             _logger.LogWarning("❌ ADMS: Dispositivo {SerialNumber} reportó error {ReturnCode} para comando {CommandId}", sn, ret, id);
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

        foreach (var line in lines)
        {
            try
            {
                var parts = line.Split('\t');
                
                string pin = "";
                DateTime checkTime = DateTime.MinValue;
                int checkType = 0;
                int verifyMethod = 3;
                bool isValid = false;

                // El formato en modo acceso u opciones de rtlog viene como "time=xxx\tpin=xxx"
                // mientras que en ATTLOG clásico es por posiciones "78\t2022-11-14 13:46:27\t0\t15"
                if (line.Contains("time=") || line.Contains("pin="))
                {
                    var logData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var part in parts)
                    {
                        var kvp = part.Split('=', 2);
                        if (kvp.Length == 2)
                        {
                            logData[kvp[0].Trim()] = kvp[1].Trim();
                        }
                    }

                    if (logData.TryGetValue("pin", out pin) && 
                        logData.TryGetValue("time", out var timeStr) && 
                        DateTime.TryParse(timeStr, out checkTime))
                    {
                        // Intentar sacar status y método
                        if (logData.TryGetValue("inoutstatus", out var inoutStr) && int.TryParse(inoutStr, out var s))
                            checkType = s;
                            
                        if (logData.TryGetValue("verifytype", out var vTypeStr) && int.TryParse(vTypeStr, out var v))
                            verifyMethod = v == 0 ? 3 : v;

                        isValid = true;
                    }
                }
                else 
                {
                    // Formato posicional
                    if (parts.Length >= 2 && DateTime.TryParse(parts[1], out checkTime))
                    {
                        pin = parts[0];
                        checkType = parts.Length > 2 && int.TryParse(parts[2], out var s) ? s : 0;
                        verifyMethod = parts.Length > 3 && int.TryParse(parts[3], out var v) ? (v == 0 ? 3 : v) : 3;
                        isValid = true;
                    }
                }

                if (isValid && !string.IsNullOrEmpty(pin))
                {
                    await _mediator.Send(new RecordAttendanceCommand(
                        pin, device.Id.Value.ToString(),
                        checkTime, verifyMethod, checkType));

                    processed++;
                    
                    // Rastrear el timestamp más reciente del batch
                    if (lastCheckTime == null || checkTime > lastCheckTime)
                        lastCheckTime = checkTime;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error línea: {Line}", line);
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
        }

        return processed;
    }
}
