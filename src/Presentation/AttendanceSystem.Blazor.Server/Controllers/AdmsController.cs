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

    // Handshake / Check configuration
    [HttpGet("cdata")]
    public IActionResult CheckData([FromQuery] string SN)
    {
         // _logger.LogInformation("ADMS Handshake received from {SerialNumber}", SN);
         return Ok("OK");
    }

    // Receive Data
    [HttpPost("cdata")]
    public async Task<IActionResult> ReceiveData([FromQuery] string SN, [FromQuery] string table)
    {
        _logger.LogInformation("📥 ADMS: Recibiendo datos de SN: {SerialNumber}, Tabla: {Table}", SN, table);
        
        if (string.IsNullOrWhiteSpace(table) || !table.Equals("ATTLOG", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("⏭️ ADMS: Ignorando tabla {Table} de {SerialNumber}", table, SN);
            return Ok("OK");
        }

        try 
        {
            var device = await _deviceRepository.GetBySerialNumberAsync(SN);
            if (device == null)
            {
                _logger.LogWarning("❌ ADMS: Datos recibidos de dispositivo desconocido SN: {SerialNumber}", SN);
                return Ok("OK");
            }
            
            _logger.LogInformation("✅ ADMS: Dispositivo encontrado - ID: {DeviceId}, Nombre: {DeviceName}", device.Id, device.Name);

            using var reader = new StreamReader(Request.Body);
            var content = await reader.ReadToEndAsync();
            
            _logger.LogInformation("📄 ADMS: Contenido recibido de {SerialNumber} - {ContentLength} caracteres", SN, content?.Length ?? 0);
            
            if (!string.IsNullOrWhiteSpace(content))
            {
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int processedCount = 0;

                foreach (var line in lines)
                {
                    try
                    {
                        // Expected format (Standard ADMS): USERID \t CHECKTIME \t STATUS \t VERIFY \t ...
                        var parts = line.Split('\t');
                        if (parts.Length >= 2)
                        {
                            var userId = parts[0];
                            if (DateTime.TryParse(parts[1], out var checkTime))
                            {
                                int checkType = 0; // Default CheckIn
                                int verifyMethod = 0; // Default Other
                                
                                // Mapping
                                if (parts.Length > 2 && int.TryParse(parts[2], out var s)) 
                                {
                                    checkType = s;
                                }

                                if (parts.Length > 3 && int.TryParse(parts[3], out var v)) 
                                {
                                    verifyMethod = v == 0 ? 3 : v;
                                }

                                var command = new RecordAttendanceCommand(
                                    userId,
                                    device.Id.Value.ToString(),
                                    checkTime,
                                    verifyMethod,
                                    checkType);

                                await _mediator.Send(command);
                                processedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing ADMS record line: {Line}", line);
                    }
                }

                if (processedCount > 0)
                {
                    device.RecordSuccessfulDownload(processedCount);
                    await _deviceRepository.UpdateAsync(device);
                    await _unitOfWork.SaveChangesAsync();
                    
                    _logger.LogInformation("✅ ADMS: Procesados {ProcessedCount} registros de {SerialNumber}", processedCount, SN);
                }
                else
                {
                    _logger.LogWarning("⚠️ ADMS: No se procesaron registros de {SerialNumber}. Contenido recibido pero sin datos válidos.", SN);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ADMS ReceiveData for SN: {SN}", SN);
        }
        
        return Ok("OK");
    }
    
    [HttpGet("getrequest")]
    public IActionResult GetRequest([FromQuery] string SN)
    {
        _logger.LogInformation("📞 ADMS: Dispositivo {SerialNumber} solicita comandos", SN);
        
        if (string.IsNullOrWhiteSpace(SN))
        {
            _logger.LogWarning("⚠️ ADMS: Solicitud sin número de serie");
            return Ok("OK");
        }
        
        if (_admsCommandService.HasPendingCommands(SN))
        {
            var (command, logId) = _admsCommandService.GetNextCommand(SN);
            if (!string.IsNullOrEmpty(command))
            {
                var cmdId = DateTime.UtcNow.Ticks.ToString();
                
                // Track execution if it has an associated log
                if (logId.HasValue)
                {
                    _admsCommandService.RegisterPendingExecution(SN, cmdId, logId.Value);
                    _logger.LogInformation("🔗 ADMS: Comando asociado con DownloadLogId: {LogId}", logId.Value);
                }

                // Return format C:ID:COMMAND
                _logger.LogInformation("📤 ADMS: Enviando comando a {SerialNumber}: {Command} (ID: {CommandId})", SN, command, cmdId);
                return Content($"C:{cmdId}:{command}", "text/plain");
            }
        }
        
        _logger.LogInformation("⏸️ ADMS: No hay comandos pendientes para {SerialNumber}", SN);
        return Ok("OK");
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

    [HttpPost("devicecmd")]
    public async Task<IActionResult> DeviceCmd([FromQuery] string SN, [FromQuery] string ID, [FromQuery] string Return)
    {
        _logger.LogInformation("✔️ ADMS: Confirmación de comando de {SerialNumber}, ID: {CommandId}, Return: {ReturnCode}", SN, ID, Return);
        
        try
        {
            if (!string.IsNullOrEmpty(ID))
            {
                var logId = _admsCommandService.GetAndRemovePendingExecution(ID);
                if (logId.HasValue)
                {
                    // This command was associated with a Manual Download Log.
                    // Now that the device has acknowledged execution, we can mark it as complete.
                    // Note: This effectively marks the "End" of the download process initiated by the user.
                    // Even if logs arrive asynchronously, this signal means the device processed the query request.
                    
                    var log = await _downloadLogRepository.GetByIdAsync(new AttendanceSystem.Domain.Aggregates.DownloadLogAggregate.DownloadLogId(logId.Value));
                    if (log != null)
                    {
                        // We mark as successful. Records count might be approximate if we don't track specifically for this batch,
                        // but usually "0" in the log with "Successful" status and a CompletedAt timestamp
                        // is enough to tell the user "We finished asking the device".
                        // Actual new records will appear in the system.
                        
                        // Optionally we could try to find how many records were added in the last minute? 
                        // Too complex. Just mark completion.
                        log.MarkAsSuccessful(0, 0); 
                        await _downloadLogRepository.UpdateAsync(log);
                        await _unitOfWork.SaveChangesAsync();
                        
                        _logger.LogInformation("✅ ADMS: DownloadLog {LogId} marcado como exitoso para {SerialNumber}", logId.Value, SN);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing DeviceCmd for SN: {SN}, ID: {ID}", SN, ID);
        }
        return Ok("OK");
    }
}
