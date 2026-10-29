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
using AttendanceSystem.Domain.Enumerations;

using AttendanceSystem.Domain.Aggregates.ExternalLogAggregate;

using AttendanceSystem.Application.Features.Attendance.Services;

namespace AttendanceSystem.Application.Features.Attendance.Commands.DownloadFromDevice;

[RequiresPermission(Permissions.DevicesDownload)]
public sealed record DownloadFromDeviceCommand(
    string DeviceId,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    bool CalculateAttendance = true,
    string? InitiatedByUserId = null,
    string? InitiatedByUserName = null,
    bool ForceFullSync = false,
    bool ClearDeviceAfterDownload = false) : IRequest<Result<DownloadResultDto>>, IRequiresDataPermissions
{
    /// <summary>
    /// Caso 13: re-descargar todo el reloj sólo lo hace el administrador, igual que vaciar su memoria. Vaciarla usa la
    /// misma limpieza segura que "Borrar tras descargar": sólo el rango leído y guardado, y sólo si la lectura fue completa.
    /// </summary>
    public IEnumerable<string> RequiredPermissions()
    {
        if (ForceFullSync) yield return Permissions.DevicesForceFullDownload;
        if (ClearDeviceAfterDownload) yield return Permissions.DevicesManage;
    }
}

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
    private readonly IExternalAttendanceLogRepository _externalLogRepository;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly IPersistenceFailureClassifier _failureClassifier;
    private readonly AttendanceSystem.Application.Features.Devices.Clock.DeviceClockService _clock;
    private readonly AttendanceSystem.Application.Features.Devices.Quarantine.PunchQuarantineService? _quarantine;

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
        IEmployeeRepository employeeRepository,
        IExternalAttendanceLogRepository externalLogRepository,
        ITimeNormalizer timeNormalizer,
        IAuthoritativeTimeZoneResolver? timeZoneResolver = null,
        IPersistenceFailureClassifier? failureClassifier = null,
        AttendanceSystem.Application.Features.Devices.Clock.DeviceClockService? clock = null,
        AttendanceSystem.Application.Features.Devices.Quarantine.PunchQuarantineService? quarantine = null)
    {
        _quarantine = quarantine;
        _failureClassifier = failureClassifier ?? DefaultPersistenceFailureClassifier.Instance;
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
        _externalLogRepository = externalLogRepository;
        _timeNormalizer = timeNormalizer;
        _timeZoneResolver = timeZoneResolver ?? DefaultAuthoritativeTimeZoneResolver.Instance;
        _clock = clock ?? new AttendanceSystem.Application.Features.Devices.Clock.DeviceClockService(_timeNormalizer, _timeZoneResolver);
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

        var requestToDate = command.ToDate ?? DateTime.UtcNow;
        DateTime? filterDate = command.ForceFullSync ? null : (command.FromDate ?? device.LastDownloadAt);

        var downloadLog = DownloadLog.Create(
            deviceId,
            downloadType,
            command.InitiatedByUserId,
            command.InitiatedByUserName,
            command.ForceFullSync ? null : command.FromDate,
            requestToDate);

        var downloadLogId = downloadLog.Id; // Guardar ID para luego

        await _downloadLogRepository.AddAsync(downloadLog, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken); // GUARDAR ESTADO INICIAL

        // == Checadas ADMS ==
        if (device.DownloadMethod == DeviceDownloadMethod.Adms)
        {
            try
            {
                var sn = device.HardwareInfo?.SerialNumber;
                _logger.LogInformation("Verificando dispositivo ADMS {Id}. SN actual: '{SerialNumber}'", deviceId, sn);

                if (string.IsNullOrEmpty(sn))
                {
                    // Intentar recargar en caso de problemas con cache
                    await _deviceRepository.ReloadAsync(device, cancellationToken);
                    sn = device.HardwareInfo?.SerialNumber;
                    _logger.LogInformation("Recargado dispositivo ADMS {Id}. SN tras recarga: '{SerialNumber}'", deviceId, sn);
                }

                if (string.IsNullOrEmpty(sn))
                {
                    return Result<DownloadResultDto>.Failure("El dispositivo ADMS no tiene número de serie registrado.");
                }

                var isAccess = device.DeviceType?.Equals("acc", StringComparison.OrdinalIgnoreCase) == true;
                var admsTimeZone = await _timeZoneResolver.ResolveForDeviceAsync(deviceId, cancellationToken);

                // Consultar sólo marcajes. CHECK/LOG y los cambios de stamp disparaban
                // reenvíos generales, fuera de la ventana histórica solicitada.
                var table = isAccess ? "Transaction" : "ATTLOG";
                string query;
                if (command.ForceFullSync || (!filterDate.HasValue && !command.ToDate.HasValue))
                {
                    query = $"DATA QUERY tablename={table},fields=*";
                }
                else
                {
                    // Las fechas de la consulta se expresan en la hora civil del reloj.
                    // Sin Hasta, cerrar la ventana en el momento de esta solicitud.
                    var fromCivil = filterDate.HasValue
                        ? ToDeviceCivil(filterDate.Value, admsTimeZone)
                        : new DateTime(1970, 1, 1);
                    var toCivil = ToDeviceCivil(requestToDate, admsTimeZone);
                    if (fromCivil > toCivil)
                    {
                        const string error = "La fecha inicial de descarga no puede ser posterior a la fecha final.";
                        downloadLog.MarkAsFailed(error);
                        await _downloadLogRepository.UpdateAsync(downloadLog, cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                        return Result<DownloadResultDto>.Failure(error);
                    }

                    query = FormattableString.Invariant($"DATA QUERY tablename={table},starttime={fromCivil:yyyy-MM-dd HH:mm:ss},endtime={toCivil:yyyy-MM-dd HH:mm:ss}");
                }

                // El resultado registrado debe corresponder a la consulta de marcajes.
                _admsCommandService.EnqueueCommand(sn!, query, downloadLogId.Value);
                _logger.LogInformation("ADMS: Consulta de marcajes encolada para SN:{SerialNumber}: {Query}", sn, query);

                _logger.LogInformation("✅ ADMS: Comandos de sincronización encolados para dispositivo SN: {SerialNumber}, DownloadLogId: {LogId}",
                    sn!, downloadLogId.Value);
                _logger.LogInformation("⏳ ADMS: Esperando que el dispositivo SN: {SerialNumber} solicite comandos vía GET /getrequest",
                    sn!);

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
        var deviceBrand = device.Brand;
        var username = device.Username;
        var password = device.Password;
        var shouldClear = device.ShouldClearAfterDownload || command.ClearDeviceAfterDownload;
        var deviceSerialNumber = device.HardwareInfo?.SerialNumber;
        var deviceName = device.Name;

        // LIMPIAR EL TRACKER COMPLETO
        // Esto asegura que no hay entidades "viejas" o "sucias" trackeadas.
        _deviceRepository.ClearChangeTracker();
        device = null; // Liberar referencia para evitar uso accidental
        downloadLog = null; // Liberar referencia

        try
        {
            // 3. Obtener el cliente especí­fico para la marca del dispositivo
            var deviceClient = _deviceClientFactory.GetClient(deviceBrand);

            // 4. Conectar al dispositivo fí­sico (Operación Larga)
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

            // Zona del reloj: con ella se interpreta la hora que marca. Antes de leer se verifica que su hora coincida
            // con la de esa zona; si se desvió, se resincroniza (y, pasado el umbral de alerta, queda una alerta).
            // Caso 18: pasado el umbral de cuarentena, los marcajes desde la descarga anterior se grabaron con la hora
            // corrida (atrasada o adelantada): van a cuarentena con la corrección. Por debajo entran, y la alerta indica
            // cómo corregirlos si la diferencia importa. Una re-descarga nunca pone en cuarentena por desfase.
            var authoritativeTimeZone = await _timeZoneResolver.ResolveForDeviceAsync(deviceId, cancellationToken);
            var clockPolicy = await _clock.PolicyAsync(cancellationToken);
            var previousCivil = filterDate.HasValue ? ToDeviceCivil(filterDate.Value, authoritativeTimeZone) : (DateTime?)null;
            bool Quarantines(TimeSpan d) => !command.ForceFullSync && _quarantine != null && d.Duration() > clockPolicy.ClockDriftQuarantine;
            string ClockImpact(TimeSpan d) => Quarantines(d)
                ? "Sus marcajes desde la descarga anterior quedaron en cuarentena para revisarlos."
                : $"Sus marcajes {(previousCivil is { } since ? $"desde el {since:dd/MM/yyyy HH:mm}" : "anteriores")} pudieron quedar corridos hasta " +
                  $"{AttendanceSystem.Application.Features.Devices.Quarantine.PunchQuarantineService.Describe(d)}; si la diferencia importa, corríjalos en Hora de relojes → Corregir marcajes de un reloj.";
            var clockCheck = await _clock.VerifyAsync(deviceClient, authoritativeTimeZone, deviceName, cancellationToken, deviceIdValue, ClockImpact);

            var arrival = command.ForceFullSync
                ? new AttendanceSystem.Application.Features.Devices.Quarantine.PunchArrivalContext(AttendanceSystem.Application.Features.Devices.Quarantine.PunchArrival.Redownload)
                : AttendanceSystem.Application.Features.Devices.Quarantine.PunchArrivalContext.Download;
            string? clockWarning = null;
            if (clockCheck.Drift is { } drift && drift.Duration() > clockPolicy.ClockDriftAlert)
            {
                clockWarning = $"El reloj estaba {(drift < TimeSpan.Zero ? "atrasado" : "adelantado")} " +
                               $"{AttendanceSystem.Application.Features.Devices.Quarantine.PunchQuarantineService.Describe(drift)}: se resincronizó. {ClockImpact(drift)}";
            }
            if (clockCheck.Drift is { } quarantineDrift && Quarantines(quarantineDrift))
            {
                // Atrasado: los marcajes nuevos quedaron con horas anteriores a la descarga previa; se lee más atrás.
                var behind = quarantineDrift < TimeSpan.Zero ? -quarantineDrift : TimeSpan.Zero;
                var margin = TimeSpan.FromDays(clockPolicy.SdkReadMarginDays);
                arrival = new AttendanceSystem.Application.Features.Devices.Quarantine.PunchArrivalContext(
                    AttendanceSystem.Application.Features.Devices.Quarantine.PunchArrival.Download, quarantineDrift, previousCivil);
                if (filterDate.HasValue && behind > TimeSpan.Zero) filterDate = filterDate.Value - behind - margin;
                if (clockCheck.Reported is { } reportedNow)
                {
                    await _quarantine!.RecordClockIncidentAsync(deviceId,
                        (previousCivil ?? reportedNow - margin) - behind - margin,
                        reportedNow, -quarantineDrift,
                        $"Al descargar, el reloj marcaba {reportedNow:dd/MM/yyyy HH:mm} cuando eran las {clockCheck.Expected:dd/MM/yyyy HH:mm}",
                        cancellationToken);
                }
            }

            // 5. Descargar registros mediante contrato explícito de resultados
            // La ventana se expresa en la hora civil del reloj (el SDK compara con la hora que marca).
            var readResult = await deviceClient.GetAttendanceLogsAsync(
                deviceIdValue,
                filterDate.HasValue ? ToDeviceCivil(filterDate.Value, authoritativeTimeZone) : null,
                ToDeviceCivil(requestToDate, authoritativeTimeZone),
                cancellationToken);

            if (readResult.Status == DeviceReadStatus.Failed)
            {
                var failedLog = await _downloadLogRepository.GetByIdAsync(downloadLogId, cancellationToken);
                if (failedLog != null)
                {
                    failedLog.MarkAsFailed(readResult.ErrorMessage ?? "Error al obtener registros del dispositivo");
                    await _downloadLogRepository.UpdateAsync(failedLog, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
                await deviceClient.DisconnectAsync(cancellationToken);
                return Result<DownloadResultDto>.Failure(readResult.ErrorMessage ?? "Error al obtener registros del dispositivo");
            }

            if (readResult.Status == DeviceReadStatus.Cancelled)
            {
                var cancelledLog = await _downloadLogRepository.GetByIdAsync(downloadLogId, cancellationToken);
                if (cancelledLog != null)
                {
                    cancelledLog.MarkAsFailed("Descarga cancelada");
                    await _downloadLogRepository.UpdateAsync(cancelledLog, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
                await deviceClient.DisconnectAsync(cancellationToken);
                return Result<DownloadResultDto>.Failure("Descarga cancelada");
            }

            var rawRecords = readResult.Records;

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

                        _logger.LogInformation("Log detectado para sucursal externa {Code}. Almacenando localmente y transfiriendo empleado {Id} a Cloud DB",
                            branchCode, actualEmployeeId);

                        var deviceSerial = deviceSerialNumber ?? command.DeviceId;
                        // Instante UTC con la zona del reloj (igual que ADMS): el destino no conoce la zona del reloj.
                        var externalUtc = _timeNormalizer.ToUtc(raw.CheckTime, authoritativeTimeZone);
                        var localExtLog = ExternalAttendanceLog.Create(
                            branchCode,
                            actualEmployeeId,
                            externalUtc,
                            raw.VerifyMethod,
                            raw.InOutMode,
                            deviceSerial);

                        await _externalLogRepository.AddAsync(localExtLog, cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);

                        var transferResult = await _logTransferService.TransferLogAsync(
                            branchCode,
                            actualEmployeeId,
                            externalUtc,
                            raw.VerifyMethod,
                            raw.InOutMode,
                            deviceSerial,
                            localExtLog.Id,
                            cancellationToken);

                        if (transferResult.IsSuccess)
                        {
                            localExtLog.MarkAsTransferred();
                        }
                        else
                        {
                            localExtLog.MarkAsFailed(transferResult.Error);
                        }

                        await _externalLogRepository.UpdateAsync(localExtLog, cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }
                }

                if (!isExternal)
                {
                    var utcCheckTime = _timeNormalizer.ToUtc(raw.CheckTime, authoritativeTimeZone);
                    var workDate = _timeNormalizer.ToWorkDate(utcCheckTime, authoritativeTimeZone);

                    domainRecords.Add(AttendanceRecord.Create(
                        EmployeeId.From(raw.UserId),
                        deviceId,
                        utcCheckTime,
                        VerifyMethod.FromValue(raw.VerifyMethod),
                        CheckType.FromValue(raw.InOutMode),
                        workDate: workDate,
                        originalCheckTime: raw.CheckTime,
                        timeZoneId: authoritativeTimeZone,
                        source: deviceBrand.ToString()
                    ));
                }
            }

            int quarantinedCount = 0, alreadyResolvedCount = 0;
            // Marcajes con hora imposible: a cuarentena (guardada antes de que el reloj pueda borrarse), no al cálculo.
            if (_quarantine != null && domainRecords.Count > 0)
            {
                var punches = domainRecords.Select(r => new AttendanceSystem.Application.Features.Devices.Quarantine.PunchCandidate(
                    r.EmployeeId.Value, r.OriginalCheckTime ?? r.CheckTime, authoritativeTimeZone, r.VerifyMethod.Id, r.CheckType.Id, r.Source ?? deviceBrand.ToString())).ToList();
                var outcome = await _quarantine.EvaluateAsync(deviceId, deviceName, punches, arrival, cancellationToken);
                quarantinedCount = outcome.Quarantined;
                alreadyResolvedCount = outcome.AlreadyResolved;
                if (outcome.Excluded.Count > 0)
                {
                    domainRecords = domainRecords.Where((_, i) => !outcome.Excluded.Contains(i)).ToList();
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

                // Deduplicar el lote leído del dispositivo
                var candidates = _deduplicationService.FilterNewRecords(domainRecords, Array.Empty<AttendanceRecord>());
                foreach (var nr in candidates) nr.DownloadLogId = downloadLogId;

                // 6. Persistir solo nuevos con la identidad única (empleado, instante), venga de este u otro
                //    dispositivo, ADMS o nube (F03-01 / F03-05). Un fallo técnico no se confunde con duplicados
                //    y aborta la descarga antes de limpiar el dispositivo.
                var ingestion = new AttendanceRecordIngestion(_attendanceRepository, _unitOfWork, _failureClassifier, _logger);
                var outcome = await ingestion.IngestAsync(candidates, cancellationToken);
                if (!outcome.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"No se pudieron guardar los marcajes descargados ({outcome.Failure!.Kind}): {outcome.Failure.Message}");
                }

                newRecordsCount = outcome.Inserted.Count;
                affectedEmployeeIds = outcome.Inserted.Select(r => r.EmployeeId.Value).Distinct().ToList();
            }

            // 7. Actualizar el dispositivo
            // RE-OBTENER instancia fresca.
            var deviceToUpdate = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);
            if (deviceToUpdate != null)
            {
                // Solo avanzar LastDownloadAt si la lectura fue completa (no parcial)
                if (readResult.Status != DeviceReadStatus.Partial)
                {
                    deviceToUpdate.RecordSuccessfulDownload(newRecordsCount, requestToDate);
                }
                else
                {
                    _logger.LogWarning("Lectura parcial para dispositivo {DeviceId}. No se avanza el cursor LastDownloadAt para reintentar el intervalo en la próxima sincronización.", deviceIdValue);
                }

                await _deviceRepository.UpdateAsync(deviceToUpdate, cancellationToken);
            }

            // Re-obtener log fresco
            var successLog = await _downloadLogRepository.GetByIdAsync(downloadLogId, cancellationToken);
            if (successLog != null)
            {
                if (readResult.Status == DeviceReadStatus.Partial)
                {
                    successLog.MarkAsFailed($"Lectura parcial ({domainRecords.Count} registros obtenidos antes de error: {readResult.ErrorMessage})");
                }
                else
                {
                    successLog.MarkAsSuccessful(domainRecords.Count, newRecordsCount);
                }
                await _downloadLogRepository.UpdateAsync(successLog, cancellationToken);
            }

            // Guardar actualizaciones finales (Device y Log)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 8. Limpiar dispositivo físico de forma segura y acotada (F04-P02 / H03)
            bool logsCleared = false;
            string? clearErrorMessage = null;

            if (shouldClear)
            {
                // Regla de seguridad: SOLO limpiar si la lectura fue completamente exitosa con datos persistidos
                if (readResult.Status == DeviceReadStatus.SuccessWithData && domainRecords.Any() && minDate.HasValue && maxDate.HasValue)
                {
                    try
                    {
                        _logger.LogInformation("Ejecutando limpieza segura acotada en {DeviceId} para el rango confirmado: {Min} - {Max}",
                            deviceIdValue, minDate.Value, maxDate.Value);

                        logsCleared = await deviceClient.ClearLogsAsync(deviceIdValue, minDate.Value, maxDate.Value, cancellationToken);
                        if (!logsCleared)
                        {
                            clearErrorMessage = "El dispositivo no soporta borrado acotado por rango o la operación fue rechazada.";
                            _logger.LogWarning("Limpieza en {DeviceId}: {Error}", deviceIdValue, clearErrorMessage);
                        }
                    }
                    catch (Exception clearEx)
                    {
                        clearErrorMessage = clearEx.Message;
                        _logger.LogError(clearEx, "Excepción durante la limpieza segura en {DeviceId}", deviceIdValue);
                    }
                }
                else if (readResult.Status == DeviceReadStatus.Partial)
                {
                    clearErrorMessage = "Limpieza omitida por seguridad debido a lectura parcial de registros.";
                    _logger.LogWarning("Limpieza en {DeviceId} omitida: {Error}", deviceIdValue, clearErrorMessage);
                }
                else
                {
                    _logger.LogInformation("No hay registros en el dispositivo {DeviceId} para limpiar en este rango.", deviceIdValue);
                }
            }

            await deviceClient.DisconnectAsync(cancellationToken);

            // Trigger Process - solo para empleados afectados
            if (affectedEmployeeIds.Any() && minDate.HasValue && maxDate.HasValue)
            {
                foreach (var empId in affectedEmployeeIds)
                {
                    // La consolidación no se encola aquí: cada marcaje insertado emite AttendanceRecordedEvent, que
                    // registra una solicitud durable por empleado desde el outbox (F04-01 / F04-05).

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
                Success: readResult.Status != DeviceReadStatus.Partial,
                ErrorMessage: readResult.ErrorMessage,
                AffectedEmployeeIds: affectedEmployeeIds,
                ReadStatus: readResult.Status,
                LogsCleared: logsCleared,
                ClearErrorMessage: clearErrorMessage,
                QuarantinedCount: quarantinedCount,
                AlreadyResolvedCount: alreadyResolvedCount,
                ClockWarning: clockWarning));
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
            catch (Exception saveEx)
            {
                // Si falla esto, ya no podemos hacer nada más que loguear a consola/archivo
                _logger.LogError(saveEx, "Error CRÃTICO guardando el log de fallo en BD.");
            }

            return Result<DownloadResultDto>.Failure($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Fecha de la ventana de descarga en la hora civil del reloj: un instante UTC (última descarga, ahora) se convierte
    /// a su zona; una fecha elegida en pantalla ya es civil y se conserva.
    /// </summary>
    private DateTime ToDeviceCivil(DateTime value, string timeZoneId)
        => DateTime.SpecifyKind(value.Kind == DateTimeKind.Utc ? _timeNormalizer.ToCivilTime(value, timeZoneId) : value, DateTimeKind.Unspecified);
}
