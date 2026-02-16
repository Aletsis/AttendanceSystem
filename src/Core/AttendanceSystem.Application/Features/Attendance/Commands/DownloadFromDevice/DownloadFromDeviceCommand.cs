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

                 // Encolar comando DATA QUERY ATTLOG (Descargar todos los logs o filtrados?)
                 // ADMS command: DATA QUERY ATTLOG StartTime=... EndTime=...
                 // Si command.FromDate es null, quizás no deberíamos restringir?
                 // Generalmente 'DATA QUERY ATTLOG' descarga todo lo que tenga.
                 // Vamos a enviar un comando simple por ahora.
                 string admsCmd = "DATA QUERY ATTLOG";
                 
                 // Si quisieramos filtrar:
                 // if (command.FromDate.HasValue) 
                 //    admsCmd += $" StartTime={command.FromDate.Value:yyyy-MM-dd HH:mm:ss}";
                 
                 // PASAMOS el LogId para rastrear cuando termine
                 _admsCommandService.EnqueueCommand(sn!, admsCmd, downloadLogId.Value);
                 
                 _logger.LogInformation("✅ ADMS: Comando '{Command}' encolado para dispositivo SN: {SerialNumber}, DownloadLogId: {LogId}", 
                     admsCmd, sn!, downloadLogId.Value);
                 _logger.LogInformation("⏳ ADMS: Esperando que el dispositivo SN: {SerialNumber} solicite comandos vía GET /iclock/getrequest", 
                     sn!);
                 _logger.LogInformation("📋 ADMS: El dispositivo debe estar configurado para comunicarse con este servidor en la URL base del sistema");
                 
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