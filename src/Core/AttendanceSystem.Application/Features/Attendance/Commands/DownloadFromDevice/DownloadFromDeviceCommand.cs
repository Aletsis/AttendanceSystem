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
    private readonly IDeviceClientFactory _deviceClientFactory; // Fábrica para resolver por marca
    private readonly IUnitOfWork _unitOfWork;
    private readonly AttendanceDeduplicationService _deduplicationService;
    private readonly ILogger<DownloadFromDeviceCommandHandler> _logger;
    private readonly IMediator _mediator;
    private readonly IDeviceLockService _deviceLockService;
    private readonly IAdmsCommandService _admsCommandService;
    private readonly IBranchRepository _branchRepository;
    private readonly ILogTransferService _logTransferService;
    private readonly IAttendanceJobScheduler _jobScheduler;
    private readonly IEmployeeRepository _employeeRepository;

    public DownloadFromDeviceCommandHandler(
        IDeviceRepository deviceRepository,
        IAttendanceRepository attendanceRepository,
        IDownloadLogRepository downloadLogRepository,
        IDeviceClientFactory deviceClientFactory,
        IUnitOfWork unitOfWork,
        AttendanceDeduplicationService deduplicationService,
        ILogger<DownloadFromDeviceCommandHandler> logger,
        IMediator mediator,
        IDeviceLockService deviceLockService,
        IAdmsCommandService admsCommandService,
        IBranchRepository branchRepository,
        ILogTransferService logTransferService,
        IAttendanceJobScheduler jobScheduler,
        IEmployeeRepository employeeRepository)
    {
        _deviceRepository = deviceRepository;
        _attendanceRepository = attendanceRepository;
        _downloadLogRepository = downloadLogRepository;
        _deviceClientFactory = deviceClientFactory;
        _unitOfWork = unitOfWork;
        _deduplicationService = deduplicationService;
        _logger = logger;
        _mediator = mediator;
        _deviceLockService = deviceLockService;
        _admsCommandService = admsCommandService;
        _branchRepository = branchRepository;
        _logTransferService = logTransferService;
        _jobScheduler = jobScheduler;
        _employeeRepository = employeeRepository;
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
        
        // 1. Obtener dispositivo INICIAL (solo para validaciÃ³n y datos bÃ¡sicos)
        var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);
        if (device == null)
            return Result<DownloadResultDto>.Failure("Dispositivo no encontrado");

        if (!device.IsActive)
            return Result<DownloadResultDto>.Failure("Dispositivo inactivo");

        // Crear registro de descarga
        var downloadType = string.IsNullOrEmpty(command.InitiatedByUserId) 
            ? DownloadType.Automatic 
            : DownloadType.Manual;
            
        var requestToDate = command.ToDate ?? DateTime.UtcNow;
        DateTime? filterDate = command.FromDate ?? device.LastDownloadAt;

        var downloadLog = DownloadLog.Create(
            deviceId,
            downloadType,
            command.InitiatedByUserId,
            command.InitiatedByUserName,
            command.FromDate,
            requestToDate);

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
                     return Result<DownloadResultDto>.Failure("El dispositivo ADMS no tiene nÃºmero de serie registrado.");
                 }

                 string admsCmd = "";
                 
                 bool isAccessMode = device.DeviceType == "acc";

                  // Determinar el comando ADMS basado en el rango calculado
                  if (filterDate.HasValue)
                  {
                      if (isAccessMode)
                      {
                          // Modo acceso: Siempre usamos DATA QUERY con rango para mayor precisión
                          var startTimeStr = filterDate.Value.ToString("yyyy-MM-ddTHH:mm:ss");
                          var endTimeStr = requestToDate.ToString("yyyy-MM-ddTHH:mm:ss");
                          
                          admsCmd = $"DATA QUERY ATTLOG StartTime={startTimeStr}\tEndTime={endTimeStr}";
                          _logger.LogInformation("ADMS: Solicitada descarga incremental (desde {From} hasta {To})", filterDate.Value, requestToDate);
                      }
                      else
                      {
                          // Modo asistencia: Usamos DATA UPDATE FROM con filtro de tiempo
                          var fromStr = filterDate.Value.ToString("yyyy-MM-dd HH:mm:ss");
                          var toStr = requestToDate.ToString("yyyy-MM-dd HH:mm:ss");
                          
                          admsCmd = $"DATA UPDATE FROM ATTLOG WHERE Time>=\"{fromStr}\" AND Time<=\"{toStr}\"";
                          _logger.LogInformation("ADMS: Solicitada descarga incremental (desde {From} hasta {To})", filterDate.Value, requestToDate);
                      }
                  }
                  else
                  {
                      // Si NO hay fecha de filtro (primera descarga), entonces sí forzamos todo
                      if (isAccessMode)
                      {
                          await _deviceRepository.ResetAttLogTimestampAsync(sn!, cancellationToken);
                          await _unitOfWork.SaveChangesAsync(cancellationToken);
                          
                          admsCmd = "DATA QUERY ATTLOG StartTime=2000-01-01T00:00:00\tEndTime=2099-12-31T23:59:59";
                          _logger.LogInformation("ADMS: Primera descarga (completa) en modo acceso");
                      }
                      else
                      {
                          admsCmd = "DATA UPDATE ATTLOG";
                          _logger.LogInformation("ADMS: Primera descarga (completa) en modo asistencia");
                      }
                  }
                 
                 if (!string.IsNullOrEmpty(admsCmd))
                 {
                     // PASAMOS el LogId para rastrear cuando termine
                     _admsCommandService.EnqueueCommand(sn!, admsCmd, downloadLogId.Value);
                     
                     _logger.LogInformation("âœ… ADMS: Comando '{Command}' encolado para dispositivo SN: {SerialNumber}, DownloadLogId: {LogId}", 
                         admsCmd, sn!, downloadLogId.Value);
                     _logger.LogInformation("â³ ADMS: Esperando que el dispositivo SN: {SerialNumber} solicite comandos vÃ­a GET /getrequest", 
                         sn!);
                     _logger.LogInformation("ðŸ“‹ ADMS: El dispositivo debe estar configurado para comunicarse con este servidor en la URL base del sistema");
                 }
                 else
                 {
                     // Si no se encolÃ³ comando (ej. ForceFullSync actuarÃ¡ vÃ­a push), damos por exitoso el log de tracking de inmediato
                     // para que la UI no se quede esperando un POST /devicecmd
                     downloadLog.MarkAsSuccessful(0, 0);
                     await _unitOfWork.SaveChangesAsync(cancellationToken);
                 }
                 
                 // NO marcamos el log como exitoso aquÃ­. Lo harÃ¡ AdmsController cuando reciba DeviceCmd.
                 // Retornamos Ã©xito indicando que se programÃ³.
                 // Nota: El frontend verÃ¡ "0 registros" pero el log quedarÃ¡ sin fecha de fin.
                 // Dependiendo del frontend, podrÃ­a mostrarse un spinner o simplemente "Iniciado".
                 
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
        var deviceBrand = device.Brand;
        var username = device.Username;
        var password = device.Password;
        var shouldClear = device.ShouldClearAfterDownload;
        // filterDate ya se definió arriba
        // requestToDate ya se definió arriba

        // LIMPIAR EL TRACKER COMPLETO
        // Esto asegura que no hay entidades "viejas" o "sucias" trackeadas.
        _deviceRepository.ClearChangeTracker();
        device = null; // Liberar referencia para evitar uso accidental
        downloadLog = null; // Liberar referencia

        try 
        {
            // 3. Obtener el cliente especÃ­fico para la marca del dispositivo
            var deviceClient = _deviceClientFactory.GetClient(deviceBrand);

            // 4. Conectar al dispositivo fÃ­sico (OperaciÃ³n Larga)
            var connected = await deviceClient.ConnectAsync(
                deviceIp, 
                devicePort, 
                username,
                password,
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

            // 5. Descargar registros
            var rawRecords = await deviceClient.GetAttendanceLogsAsync(
                deviceIdValue, 
                filterDate, 
                requestToDate,
                cancellationToken);

            // 5. Convertir a entidades de dominio
            // Nota: Usamos deviceId (ValueObject) que creamos al principio
            var domainRecords = new List<AttendanceRecord>();
            
            // Obtener todas las sucursales externas para filtrar rápido
            var allBranches = await _branchRepository.GetAllAsync(cancellationToken);
            var externalBranches = allBranches.Where(b => b.IsExternal).ToList();

            foreach (var raw in rawRecords)
            {
                // Si el ID tiene al menos 4 caracteres (3 de código + al menos 1 de ID)
                // y los primeros 3 coinciden con una sucursal externa
                bool isExternal = false;
                if (raw.UserId.Length > 3)
                {
                    var branchCode = raw.UserId.Substring(0, 3);
                    var externalBranch = externalBranches.FirstOrDefault(b => b.Code == branchCode);
                    if (externalBranch != null)
                    {
                        isExternal = true;
                        var actualEmployeeId = raw.UserId.Substring(3);
                        
                        _logger.LogInformation("Log detectado para sucursal externa {Code}. Transfiriendo empleado {Id} a {Host}", 
                            branchCode, actualEmployeeId, externalBranch.ExternalHost);

                        // Transferir log (esto podría ser asíncrono en segundo plano si son muchos)
                        await _logTransferService.TransferLogAsync(
                            externalBranch.ExternalHost!,
                            actualEmployeeId,
                            raw.CheckTime,
                            raw.VerifyMethod,
                            raw.InOutMode,
                            cancellationToken);
                    }
                }

                if (!isExternal)
                {
                    domainRecords.Add(AttendanceRecord.Create(
                        EmployeeId.From(raw.UserId),
                        deviceId,
                        raw.CheckTime,
                        VerifyMethod.FromValue(raw.VerifyMethod),
                        CheckType.FromValue(raw.InOutMode)
                    ));
                }
            }

            DateTime? minDate = null;
            DateTime? maxDate = null;
            int newRecordsCount = 0;
            var affectedEmployeeIds = new List<string>();

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
                    affectedEmployeeIds = newRecords.Select(r => r.EmployeeId.Value).Distinct().ToList();
                    
                    // 6. Persistir solo nuevos
                    foreach (var nr in newRecords) nr.DownloadLogId = downloadLogId;
                    
                    await _attendanceRepository.AddRangeAsync(newRecords, cancellationToken);
                    // Guardar attendance records. No deberÃ­a haber conflictos aquÃ­.
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }

            // 7. Actualizar el dispositivo
            // RE-OBTENER instancia fresca. Esto es lo mÃ¡s importante para la concurrencia.
            var deviceToUpdate = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);
            if (deviceToUpdate != null)
            {
                // Aplicar logic
                if (newRecordsCount > 0 || domainRecords.Count > 0)
                {
                     deviceToUpdate.RecordSuccessfulDownload(newRecordsCount, requestToDate);
                }
                else
                {
                     // Mantener lÃ³gica de negocio
                     deviceToUpdate.RecordSuccessfulDownload(0, requestToDate);
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

            // 8. Opcional: Limpiar dispositivo fÃ­sico
            if (shouldClear)
            {
                await deviceClient.ClearLogsAsync(deviceIdValue, cancellationToken: cancellationToken);
            }

            await deviceClient.DisconnectAsync(cancellationToken);

            // Trigger Process - solo para empleados afectados
            if (affectedEmployeeIds.Any() && minDate.HasValue && maxDate.HasValue)
            {
                foreach (var empId in affectedEmployeeIds)
                {
                    if (command.CalculateAttendance)
                    {
                        // Expand range by 1 day back to ensure night shifts are caught correctly
                        var processStartDate = minDate.Value.AddDays(-1);
                        _jobScheduler.EnqueueAttendanceProcessing(processStartDate, maxDate.Value, empId);
                    }

                    // Queue missing biometrics sync
                    var emp = await _employeeRepository.GetByIdAsync(EmployeeId.From(empId), cancellationToken);
                    if (emp != null)
                    {
                        bool isMissingBiometrics = !emp.Fingerprints.Any() && 
                                                   string.IsNullOrEmpty(emp.DevicePassword) && 
                                                   string.IsNullOrEmpty(emp.CardNumber) && 
                                                   string.IsNullOrEmpty(emp.FaceTemplate);
                        
                        if (isMissingBiometrics)
                        {
                            _logger.LogInformation("Biometría faltante detectada para empleado {EmployeeId}. Encolando sincronización.", empId);
                            _jobScheduler.EnqueueBiometricSync(deviceIdValue, empId);
                        }
                    }
                }
            }

            return Result<DownloadResultDto>.Success(new DownloadResultDto(
                deviceIdValue,
                domainRecords.Count,
                DateTime.UtcNow,
                minDate,
                maxDate,
                AffectedEmployeeIds: affectedEmployeeIds));
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
                 // Si falla esto, ya no podemos hacer nada mÃ¡s que loguear a consola/archivo
                 _logger.LogError(saveEx, "Error CRÃTICO guardando el log de fallo en BD.");
            }
            
            return Result<DownloadResultDto>.Failure($"Error: {ex.Message}");
        }
    }
}
