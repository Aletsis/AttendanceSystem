using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.DeviceAggregate;
using AttendanceSystem.Domain.Aggregates.DownloadLogAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

using AttendanceSystem.Domain.Services;
namespace AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;

public sealed record DownloadFromDeviceCommand(
    string DeviceId, 
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    bool CalculateAttendance = true,
    string? InitiatedByUserId = null,
    string? InitiatedByUserName = null) : IRequest<Result<DownloadResultDto>>;

public sealed class DownloadFromDeviceCommandHandler 
    : IRequestHandler<DownloadFromDeviceCommand, Result<DownloadResultDto>>
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IDownloadLogRepository _downloadLogRepository;
    private readonly IZKTecoDeviceClient _deviceClient; // Puerto (Interfaz)
    private readonly IUnitOfWork _unitOfWork;
    private readonly AttendanceDeduplicationService _deduplicationService;
    private readonly ILogger<DownloadFromDeviceCommandHandler> _logger;
    private readonly IMediator _mediator;
    private readonly IDeviceLockService _deviceLockService;
    private readonly IAdmsCommandService _admsCommandService;

    public DownloadFromDeviceCommandHandler(
        IDeviceRepository deviceRepository,
        IAttendanceRepository attendanceRepository,
        IDownloadLogRepository downloadLogRepository,
        IZKTecoDeviceClient deviceClient,
        IUnitOfWork unitOfWork,
        AttendanceDeduplicationService deduplicationService,
        ILogger<DownloadFromDeviceCommandHandler> logger,
        IMediator mediator,
        IDeviceLockService deviceLockService,
        IAdmsCommandService admsCommandService)
    {
        _deviceRepository = deviceRepository;
        _attendanceRepository = attendanceRepository;
        _downloadLogRepository = downloadLogRepository;
        _deviceClient = deviceClient;
        _unitOfWork = unitOfWork;
        _deduplicationService = deduplicationService;
        _logger = logger;
        _mediator = mediator;
        _deviceLockService = deviceLockService;
        _admsCommandService = admsCommandService;
    }

    public async Task<Result<DownloadResultDto>> Handle(
        DownloadFromDeviceCommand command, 
        CancellationToken cancellationToken)
    {
        Result<DownloadResultDto> result = Result<DownloadResultDto>.Failure("Error desconocido iniciando descarga");

        await _deviceLockService.ExecuteWithLockAsync(command.DeviceId, async () =>
        {
            result = await HandleInternal(command, cancellationToken);
        }, cancellationToken);

        return result;
    }

    private async Task<Result<DownloadResultDto>> HandleInternal(
        DownloadFromDeviceCommand command, 
        CancellationToken cancellationToken)
    {
        var deviceId = DeviceId.From(command.DeviceId);
        
        // 1. Obtener dispositivo INICIAL (solo para validación y datos básicos)
        var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);
        if (device == null)
            return Result<DownloadResultDto>.Failure("Dispositivo no encontrado");

        if (!device.IsActive)
            return Result<DownloadResultDto>.Failure("Dispositivo inactivo");

        // Crear registro de descarga
        var downloadType = string.IsNullOrEmpty(command.InitiatedByUserId) 
            ? DownloadType.Automatic 
            : DownloadType.Manual;
            
        var downloadLog = DownloadLog.Create(
            deviceId,
            downloadType,
            command.InitiatedByUserId,
            command.InitiatedByUserName,
            command.FromDate,
            command.ToDate);

        var downloadLogId = downloadLog.Id; // Guardar ID para luego

        await _downloadLogRepository.AddAsync(downloadLog, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken); // GUARDAR ESTADO INICIAL

        // == CHECK FOR ADMS ==
        if (device.DownloadMethod == Domain.Enumerations.DeviceDownloadMethod.Adms)
        {
             try
             {
                 var sn = device.HardwareInfo?.SerialNumber;
                 _logger.LogInformation("Verificando dispositivo ADMS {Id}. SN actual: '{SerialNumber}'", deviceId, sn);

                 if (string.IsNullOrEmpty(sn))
                 {
                     // Attempt reload in case of caching issues
                     await _deviceRepository.ReloadAsync(device, cancellationToken);
                     sn = device.HardwareInfo?.SerialNumber;
                     _logger.LogInformation("Recargado dispositivo ADMS {Id}. SN tras recarga: '{SerialNumber}'", deviceId, sn);
                 }

                 if (string.IsNullOrEmpty(sn))
                 {
                     return Result<DownloadResultDto>.Failure("El dispositivo ADMS no tiene número de serie registrado.");
                 }

                 string admsCmd = "";
                 
                 bool isAccessMode = device.DeviceType == "acc";

                 if (!command.FromDate.HasValue)
                 {
                     // Descarga general / Forzar todo
                     if (isAccessMode)
                     {
                         // 1. Resetear el stamp en BD a null/0
                         //    Así el PRÓXIMO /push que haga el reloj recibirá stamp=0 y enviará todo su historial
                         await _deviceRepository.ResetAttLogTimestampAsync(sn!, cancellationToken);
                         await _unitOfWork.SaveChangesAsync(cancellationToken);
                         
                         // En lugar de forzar punteros en RAM, utilizamos el comando puro de extracción soportado
                         // nativamente por los equipos ADMS. (Como se ve en el repo ZKTecoADMS de referencia).
                         // Esto obliga al dispositivo a devolver el historial entero explícitamente y lo vuelca en cdata o POST.
                         admsCmd = "DATA QUERY ATTLOG StartTime=2000-01-01T00:00:00\tEndTime=2099-12-31T23:59:59";
                         
                         _logger.LogInformation("ADMS: Descarga completa en modo acceso — stamp reseteado a 0 y comando DATA QUERY ATTLOG encolado para {SN}", sn);
                     }
                     else
                     {
                         admsCmd = "DATA UPDATE ATTLOG";
                         _logger.LogInformation("ADMS: Solicitada descarga completa, enviando DATA UPDATE ATTLOG");
                     }
                 }
                 else
                 {
                     if (isAccessMode)
                     {
                         // Filtro manual estricto soportado por SDK ZKTeco:
                         var startTimeStr = command.FromDate.Value.ToString("yyyy-MM-ddTHH:mm:ss");
                         var endTimeStr = (command.ToDate ?? DateTime.Now.AddDays(1)).ToString("yyyy-MM-ddTHH:mm:ss");
                         
                         admsCmd = $"DATA QUERY ATTLOG StartTime={startTimeStr}\tEndTime={endTimeStr}";
                         
                         _logger.LogInformation("ADMS: Solicitada descarga parcial (desde {Date}), enviando DATA QUERY ATTLOG", command.FromDate.Value);
                     }
                     else
                     {
                         // Modo asistencia: tabla ATTLOG, soporta filtro de fechas
                         var fromStr = command.FromDate.Value.ToString("yyyy-MM-dd HH:mm:ss");
                         if (command.ToDate.HasValue)
                         {
                             var toStr = command.ToDate.Value.ToString("yyyy-MM-dd HH:mm:ss");
                             admsCmd = $"DATA UPDATE FROM ATTLOG WHERE Time>=\"{fromStr}\" AND Time<=\"{toStr}\"";
                             _logger.LogInformation("ADMS: Solicitada descarga parcial (desde {From} hasta {To}), enviando comando con rango", command.FromDate, command.ToDate);
                         }
                         else
                         {
                             admsCmd = $"DATA UPDATE FROM ATTLOG WHERE Time>\"{fromStr}\"";
                             _logger.LogInformation("ADMS: Solicitada descarga parcial (desde {Date}), enviando comando desde fecha", command.FromDate);
                         }
                     }
                 }
                 
                 if (!string.IsNullOrEmpty(admsCmd))
                 {
                     // PASAMOS el LogId para rastrear cuando termine
                     _admsCommandService.EnqueueCommand(sn!, admsCmd, downloadLogId.Value);
                     
                     _logger.LogInformation("✅ ADMS: Comando '{Command}' encolado para dispositivo SN: {SerialNumber}, DownloadLogId: {LogId}", 
                         admsCmd, sn!, downloadLogId.Value);
                     _logger.LogInformation("⏳ ADMS: Esperando que el dispositivo SN: {SerialNumber} solicite comandos vía GET /getrequest", 
                         sn!);
                     _logger.LogInformation("📋 ADMS: El dispositivo debe estar configurado para comunicarse con este servidor en la URL base del sistema");
                 }
                 else
                 {
                     // Si no se encoló comando (ej. ForceFullSync actuará vía push), damos por exitoso el log de tracking de inmediato
                     // para que la UI no se quede esperando un POST /devicecmd
                     downloadLog.MarkAsSuccessful(0, 0);
                     await _unitOfWork.SaveChangesAsync(cancellationToken);
                 }
                 
                 // NO marcamos el log como exitoso aquí. Lo hará AdmsController cuando reciba DeviceCmd.
                 // Retornamos éxito indicando que se programó.
                 // Nota: El frontend verá "0 registros" pero el log quedará sin fecha de fin.
                 // Dependiendo del frontend, podría mostrarse un spinner o simplemente "Iniciado".
                 
                 return Result<DownloadResultDto>.Success(new DownloadResultDto(
                     deviceId.Value,
                     0,
                     DateTime.UtcNow,
                     null,
                     null));
             }
             catch (Exception ex)
             {
                 return Result<DownloadResultDto>.Failure($"Error encolando comando ADMS: {ex.Message}");
             }
        }

        // Capturar datos necesarios antes de limpiar el tracker
        var deviceIp = device.IpAddress;
        var devicePort = device.Port;
        var deviceIdValue = device.Id.Value;
        var shouldClear = device.ShouldClearAfterDownload;
        DateTime? filterDate = command.FromDate ?? device.LastDownloadAt;

        // LIMPIAR EL TRACKER COMPLETO
        // Esto asegura que no hay entidades "viejas" o "sucias" trackeadas.
        _deviceRepository.ClearChangeTracker();
        device = null; // Liberar referencia para evitar uso accidental
        downloadLog = null; // Liberar referencia

        try 
        {
            // 3. Conectar al dispositivo físico (Operación Larga)
            var connected = await _deviceClient.ConnectAsync(
                deviceIp, 
                devicePort, 
                cancellationToken);

            if (!connected)
            {
                // Re-obtener log fresco para marcar error
                var failedLog = await _downloadLogRepository.GetByIdAsync(downloadLogId, cancellationToken);
                if (failedLog != null)
                {
                    failedLog.MarkAsFailed("No se pudo conectar al dispositivo");
                    await _downloadLogRepository.UpdateAsync(failedLog, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
                return Result<DownloadResultDto>.Failure("No se pudo conectar al dispositivo");
            }

            // 4. Descargar registros
            var rawRecords = await _deviceClient.GetAttendanceLogsAsync(
                deviceIdValue, 
                filterDate, 
                command.ToDate,
                cancellationToken);

            // 5. Convertir a entidades de dominio
            // Nota: Usamos deviceId (ValueObject) que creamos al principio
            var domainRecords = rawRecords.Select(raw => 
                AttendanceRecord.Create(
                    EmployeeId.From(raw.UserId),
                    deviceId,
                    raw.CheckTime,
                    VerifyMethod.FromValue(raw.VerifyMethod),
                    CheckType.FromValue(raw.InOutMode)
                )).ToList();

            DateTime? minDate = null;
            DateTime? maxDate = null;
            int newRecordsCount = 0;

            if (domainRecords.Any())
            {
                // Verificar existencia en base de datos para el rango
                minDate = domainRecords.Min(r => r.CheckTime);
                maxDate = domainRecords.Max(r => r.CheckTime);

                var existingRecords = await _attendanceRepository.GetByDeviceAndDateRangeAsync(
                    deviceId, minDate.Value, maxDate.Value, cancellationToken);
                
                // Usar servicio de dominio para filtrar nuevos
                var newRecords = _deduplicationService.FilterNewRecords(domainRecords, existingRecords);
                newRecordsCount = newRecords.Count;
                
                if (newRecords.Any())
                {
                    // 6. Persistir solo nuevos
                    await _attendanceRepository.AddRangeAsync(newRecords, cancellationToken);
                    // Guardar attendance records. No debería haber conflictos aquí.
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }

            // 7. Actualizar el dispositivo
            // RE-OBTENER instancia fresca. Esto es lo más importante para la concurrencia.
            var deviceToUpdate = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);
            if (deviceToUpdate != null)
            {
                // Aplicar logic
                if (newRecordsCount > 0 || domainRecords.Count > 0)
                {
                     deviceToUpdate.RecordSuccessfulDownload(newRecordsCount);
                }
                else
                {
                     // Mantener lógica de negocio
                     deviceToUpdate.RecordSuccessfulDownload(0);
                }
                
                await _deviceRepository.UpdateAsync(deviceToUpdate, cancellationToken);
            }

            // Re-obtener log fresco
            var successLog = await _downloadLogRepository.GetByIdAsync(downloadLogId, cancellationToken);
            if (successLog != null)
            {
                successLog.MarkAsSuccessful(domainRecords.Count, newRecordsCount);
                await _downloadLogRepository.UpdateAsync(successLog, cancellationToken);
            }
            
            // Guardar actualizaciones finales (Device y Log)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 8. Opcional: Limpiar dispositivo físico
            if (shouldClear)
            {
                await _deviceClient.ClearLogsAsync(deviceIdValue, cancellationToken);
            }

            await _deviceClient.DisconnectAsync(cancellationToken);

            // Trigger Process
            if (command.CalculateAttendance && minDate.HasValue && maxDate.HasValue)
            {
                await _mediator.Send(new AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.ProcessDailyAttendanceCommand(minDate.Value, maxDate.Value), cancellationToken);
            }

            return Result<DownloadResultDto>.Success(new DownloadResultDto(
                deviceIdValue,
                domainRecords.Count,
                DateTime.UtcNow,
                minDate,
                maxDate));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error descargando del dispositivo {DeviceId}", deviceId);

            // Manejo Robusto de Errores
            try 
            {
                // Limpiar cualquier estado sucio que haya quedado
                _deviceRepository.ClearChangeTracker();
                
                // Intentar recuperar el log y marcar error
                var errorLog = await _downloadLogRepository.GetByIdAsync(downloadLogId, cancellationToken);
                if (errorLog != null)
                {
                    errorLog.MarkAsFailed(ex.Message);
                    await _downloadLogRepository.UpdateAsync(errorLog, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }
            catch(Exception saveEx) 
            {
                 // Si falla esto, ya no podemos hacer nada más que loguear a consola/archivo
                 _logger.LogError(saveEx, "Error CRÍTICO guardando el log de fallo en BD.");
            }
            
            return Result<DownloadResultDto>.Failure($"Error: {ex.Message}");
        }
    }
}