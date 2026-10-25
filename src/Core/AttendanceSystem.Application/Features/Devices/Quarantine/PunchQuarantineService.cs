using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AttendanceSystem.Application.Features.Devices.Quarantine;

/// <summary>Marcaje recibido de un reloj, con la hora civil que reportó y la zona en que se interpreta.</summary>
public sealed record PunchCandidate(string EmployeeId, DateTime CivilTime, string TimeZoneId, int VerifyMethodCode, int CheckTypeCode, string Source);

/// <summary>Caso 18: cómo llegó el lote; decide qué reglas aplican.</summary>
public enum PunchArrival
{
    /// <summary>ADMS envía cada checada al momento: lo viejo es sospechoso (R4, R5).</summary>
    Realtime,
    /// <summary>Descarga normal (desde la última descarga). Con SDK se conoce el desfase del reloj antes de leer.</summary>
    Download,
    /// <summary>Re-descarga o consulta del historial: lo viejo es normal; sólo aplican las reglas de la hora misma.</summary>
    Redownload,
}

/// <param name="SdkClockBehind">Atraso del reloj medido al conectarse por SDK (positivo), si superó la tolerancia.</param>
/// <param name="SdkPreviousDownloadCivil">Hora civil del reloj en la descarga anterior; desde ahí los marcajes nuevos están corridos.</param>
public sealed record PunchArrivalContext(PunchArrival Arrival, TimeSpan? SdkClockBehind = null, DateTime? SdkPreviousDownloadCivil = null, string? DeviceSerial = null)
{
    public static PunchArrivalContext Download { get; } = new(PunchArrival.Download);
}

/// <summary>
/// Resultado de evaluar un lote: posiciones que no deben ingresarse (en cuarentena ahora o ya resueltas antes) y, en
/// tiempo real, el atraso detectado del reloj para resincronizarlo.
/// </summary>
/// <param name="ClockBehind">En tiempo real: atraso del reloj (R4 si superó el umbral, R5 si sólo alerta).</param>
/// <param name="LagQuarantined">En tiempo real: hubo marcajes en cuarentena por R4 (el reloj estuvo atrasado).</param>
/// <param name="PreviousLatestCivil">Último marcaje bueno del reloj antes del lote (desde ahí pudo atrasarse).</param>
public sealed record QuarantineOutcome(IReadOnlySet<int> Excluded, int Quarantined, int AlreadyResolved, TimeSpan? ClockBehind,
    bool LagQuarantined = false, DateTime? PreviousLatestCivil = null)
{
    public static QuarantineOutcome None { get; } = new(new HashSet<int>(), 0, 0, null);
}

/// <summary>
/// Cuarentena de marcajes (casos 14 y 18). Un marcaje en cuarentena no entra al cálculo hasta que el Administrador lo
/// libera con la hora corregida o lo descarta; ninguna regla lo corrige sola. Por cada candidato, en este orden:
/// <list type="number">
/// <item>Ya ingresado (misma identidad, aunque esté anulado): se deja a la ingesta, que lo reconoce como duplicado.</item>
/// <item><b>Memoria</b>: el mismo reloj + empleado + hora reportada ya pasó por cuarentena (pendiente, liberado o
/// descartado): no vuelve a entrar; así una re-descarga no deshace lo resuelto.</item>
/// <item><b>Incidente</b>: su hora reportada cae en un periodo en que el reloj estuvo atrasado.</item>
/// <item><b>R1</b> fecha anterior a la mínima del reloj; <b>R2</b> en el futuro; <b>R3</b> la hora retrocede respecto al
/// marcaje anterior del reloj (salto). Aplican siempre.</item>
/// <item><b>Atraso al descargar</b> (SDK, descarga normal): el reloj estaba atrasado al conectarse; los marcajes
/// nuevos desde la descarga anterior están corridos.</item>
/// <item><b>R4</b> (ADMS en tiempo real): marcaje de hace más del umbral cuando el reloj estaba conectado desde antes
/// de esa hora (lo habría enviado entonces). <b>R5</b>: un desfase menor sólo alerta y resincroniza.</item>
/// </list>
/// Las reglas de desfase no aplican en re-descargas, donde lo viejo es normal.
/// </summary>
public sealed class PunchQuarantineService
{
    public const string RuleMinDate = "R1 Fecha mínima";
    public const string RuleFuture = "R2 Futuro";
    public const string RuleBackwardJump = "R3 Salto hacia atrás";
    public const string RuleRealtimeLag = "R4 Atraso en tiempo real";
    public const string RuleSdkBehind = "Atraso al descargar";
    public const string RuleIncident = "Periodo con hora atrasada";

    private readonly IQuarantinedPunchRepository _quarantine;
    private readonly ISystemAlertRepository? _alerts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PunchQuarantineService> _logger;
    private readonly IDeviceRepository? _devices;
    private readonly ISystemConfigurationRepository? _configuration;
    private readonly IAttendanceRepository? _records;
    private readonly IDeviceClockIncidentRepository? _incidents;
    private readonly IDeviceHeartbeatTracker? _heartbeats;

    public PunchQuarantineService(
        IQuarantinedPunchRepository quarantine,
        IUnitOfWork unitOfWork,
        ITimeNormalizer timeNormalizer,
        ISystemAlertRepository? alerts = null,
        ILogger<PunchQuarantineService>? logger = null,
        TimeProvider? timeProvider = null,
        IDeviceRepository? devices = null,
        ISystemConfigurationRepository? configuration = null,
        IAttendanceRepository? records = null,
        IDeviceClockIncidentRepository? incidents = null,
        IDeviceHeartbeatTracker? heartbeats = null)
    {
        _quarantine = quarantine;
        _unitOfWork = unitOfWork;
        _timeNormalizer = timeNormalizer;
        _alerts = alerts;
        _logger = logger ?? NullLogger<PunchQuarantineService>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _devices = devices;
        _configuration = configuration;
        _records = records;
        _incidents = incidents;
        _heartbeats = heartbeats;
    }

    /// <summary>Hora civil actual de la zona.</summary>
    public DateTime ClockNow(string timeZoneId) =>
        DateTime.SpecifyKind(_timeNormalizer.ToCivilTime(_timeProvider.GetUtcNow().UtcDateTime, timeZoneId), DateTimeKind.Unspecified);

    public async Task<PunchQuarantinePolicy> PolicyAsync(CancellationToken cancellationToken) =>
        _configuration != null && await _configuration.GetConfigurationAsync(cancellationToken) is { } config
            ? config.QuarantinePolicy
            : PunchQuarantinePolicy.Default;

    /// <summary>Compatibilidad: evalúa como descarga normal y devuelve las posiciones que no deben ingresarse.</summary>
    public async Task<IReadOnlySet<int>> QuarantineImpossibleAsync(DeviceId deviceId, string deviceName, IReadOnlyList<PunchCandidate> candidates, CancellationToken cancellationToken = default) =>
        (await EvaluateAsync(deviceId, deviceName, candidates, PunchArrivalContext.Download, cancellationToken)).Excluded;

    private sealed record Flag(string Rule, string Reason, int? SuggestedOffsetMinutes);

    /// <summary>Aplica la memoria, los incidentes y las reglas al lote, en el orden en que el reloj lo entregó.</summary>
    public async Task<QuarantineOutcome> EvaluateAsync(
        DeviceId deviceId,
        string deviceName,
        IReadOnlyList<PunchCandidate> candidates,
        PunchArrivalContext context,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0) return QuarantineOutcome.None;

        var policy = await PolicyAsync(cancellationToken);
        var device = _devices != null ? await _devices.GetByIdAsync(deviceId, cancellationToken) : null;
        var minDate = device?.MinimumPunchDate(policy.MinDateDaysBeforeDevice) ?? PunchTimePlausibility.EarliestValid;
        if (minDate < PunchTimePlausibility.EarliestValid) minDate = PunchTimePlausibility.EarliestValid;

        // 1. Ya ingresados (incluidos anulados): la ingesta los reconoce; sirven de contexto para el salto.
        var keys = candidates.Select(c => (EmployeeId.From(c.EmployeeId), _timeNormalizer.ToUtc(Civil(c), c.TimeZoneId))).ToList();
        var known = _records != null
            ? ((await _records.GetByIdentitiesAsync(keys.Distinct().ToList(), cancellationToken)) ?? Array.Empty<AttendanceRecord>()).Select(r => (r.EmployeeId, r.CheckTime)).ToHashSet()
            : new HashSet<(EmployeeId, DateTime)>();

        // 2. Memoria de cuarentena.
        var reportedKeys = candidates.Select(c => (c.EmployeeId, Civil(c))).Distinct().ToList();
        var resolved = await _quarantine.GetExistingAsync(deviceId, reportedKeys, cancellationToken);
        var incidents = _incidents != null ? await _incidents.GetForDeviceAsync(deviceId, cancellationToken) : Array.Empty<DeviceClockIncident>();

        // Contexto del salto hacia atrás: en tiempo real, el último marcaje del reloj; en descargas, el propio lote.
        DateTime? maxSoFar = null;
        if (context.Arrival == PunchArrival.Realtime && _records != null && await _records.GetLatestByDeviceAsync(deviceId, cancellationToken) is { } latest)
        {
            maxSoFar = DateTime.SpecifyKind(latest.GetCivilTime(candidates[0].TimeZoneId), DateTimeKind.Unspecified);
        }
        var previousLatest = maxSoFar;
        var connectedSinceUtc = context.Arrival == PunchArrival.Realtime
            ? _heartbeats?.GetConnectedSince(context.DeviceSerial, deviceId.Value)
            : null;

        var flags = new Dictionary<int, Flag>();
        var excluded = new HashSet<int>();
        int alreadyResolved = 0;
        TimeSpan? realtimeBehind = null;
        var nowByZone = new Dictionary<string, DateTime>();

        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var civil = Civil(c);
            if (!nowByZone.TryGetValue(c.TimeZoneId, out var now)) nowByZone[c.TimeZoneId] = now = ClockNow(c.TimeZoneId);

            if (known.Contains(keys[i]))
            {
                if (maxSoFar == null || civil > maxSoFar) maxSoFar = civil;
                continue;
            }
            if (resolved.Contains((c.EmployeeId, civil)))
            {
                excluded.Add(i);
                alreadyResolved++;
                continue;
            }

            Flag? flag = null;
            if (incidents.FirstOrDefault(x => x.Contains(civil)) is { } incident)
            {
                flag = new Flag(RuleIncident, $"Su hora ({civil:dd/MM/yyyy HH:mm}) cae en el periodo en que el reloj estuvo atrasado: {incident.Reason}", incident.OffsetMinutes);
            }
            else if (civil < minDate)
            {
                flag = new Flag(RuleMinDate, $"Fecha {civil:dd/MM/yyyy HH:mm} anterior a la fecha mínima del reloj ({minDate:dd/MM/yyyy}): el reloj perdió su fecha.", null);
            }
            else if (civil - now > TimeSpan.FromMinutes(policy.FutureToleranceMinutes))
            {
                flag = new Flag(RuleFuture, $"Hora {civil:dd/MM/yyyy HH:mm} posterior a la actual del reloj ({now:dd/MM/yyyy HH:mm}): el reloj está adelantado.", null);
            }
            else if (maxSoFar is { } max && max - civil > TimeSpan.FromMinutes(policy.BackwardJumpMinutes))
            {
                flag = new Flag(RuleBackwardJump, $"La hora retrocedió de {max:dd/MM/yyyy HH:mm} a {civil:dd/MM/yyyy HH:mm} entre marcajes del reloj: alguien la cambió o se reinició.", null);
            }
            else if (context.Arrival == PunchArrival.Download && context.SdkClockBehind is { } behind &&
                     (context.SdkPreviousDownloadCivil is not { } previous || civil >= previous - behind - TimeSpan.FromDays(policy.SdkReadMarginDays)))
            {
                flag = new Flag(RuleSdkBehind, $"Al descargar, el reloj estaba atrasado {Describe(behind)}: este marcaje se registró con esa hora corrida.", Minutes(behind));
            }
            else if (context.Arrival == PunchArrival.Realtime)
            {
                var lag = now - civil;
                var newest = previousLatest == null || civil > previousLatest;
                var connectedAtPunch = connectedSinceUtc is { } since && _timeNormalizer.ToUtc(civil, c.TimeZoneId) >= since;
                if (newest && connectedAtPunch && lag > TimeSpan.FromMinutes(policy.RealtimeLagMinutes))
                {
                    flag = new Flag(RuleRealtimeLag, $"Llegó en tiempo real con hora de hace {Describe(lag)} aunque el reloj estaba conectado: su hora está atrasada.", Minutes(lag));
                    realtimeBehind = realtimeBehind is { } r && r > lag ? r : lag;
                }
                else if (newest && connectedAtPunch && lag > TimeSpan.FromMinutes(policy.ClockDriftAlertMinutes))
                {
                    // R5: desfase menor; el marcaje entra y el reloj se resincroniza.
                    realtimeBehind ??= lag;
                }
            }

            if (flag != null)
            {
                flags[i] = flag;
                excluded.Add(i);
            }
            else if (maxSoFar == null || civil > maxSoFar)
            {
                maxSoFar = civil;
            }
        }

        var quarantined = await SaveAsync(deviceId, deviceName, candidates, flags, cancellationToken);
        if (alreadyResolved > 0)
        {
            _logger.LogInformation("{Count} marcaje(s) del reloj {Device} ya se habían resuelto en cuarentena; no se ingresaron de nuevo.", alreadyResolved, deviceName);
        }
        return new QuarantineOutcome(excluded, quarantined, alreadyResolved, realtimeBehind,
            flags.Values.Any(f => f.Rule == RuleRealtimeLag), previousLatest);
    }

    private async Task<int> SaveAsync(DeviceId deviceId, string deviceName, IReadOnlyList<PunchCandidate> candidates, Dictionary<int, Flag> flags, CancellationToken cancellationToken)
    {
        if (flags.Count == 0) return 0;
        var added = new HashSet<(string, DateTime)>();
        var punches = new List<QuarantinedPunch>();
        foreach (var (i, flag) in flags)
        {
            var c = candidates[i];
            if (!added.Add((c.EmployeeId, Civil(c)))) continue;
            punches.Add(QuarantinedPunch.Create(c.EmployeeId, deviceId, c.CivilTime, c.TimeZoneId, c.VerifyMethodCode, c.CheckTypeCode, c.Source,
                flag.Reason, flag.Rule, flag.SuggestedOffsetMinutes));
        }

        await _quarantine.AddRangeAsync(punches, cancellationToken);
        if (_alerts != null)
        {
            var rules = string.Join(", ", punches.Select(p => p.Rule).Distinct());
            await _alerts.AddAsync(SystemAlert.Create(AlertType.PunchQuarantined, deviceId.Value,
                $"El reloj {deviceName} envió {punches.Count} marcaje(s) con hora sospechosa ({rules}); quedaron en cuarentena. {punches[0].Reason}"), cancellationToken);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        AttendanceTelemetry.QuarantinedPunches.Add(punches.Count);
        _logger.LogWarning("ALERTA: {Count} marcaje(s) del reloj {Device} quedaron en cuarentena (p. ej. {Reason}).", punches.Count, deviceName, punches[0].Reason);
        return punches.Count;
    }

    /// <summary>
    /// Caso 18: guarda el periodo (en horas reportadas por el reloj) en que estuvo atrasado, para que cualquier marcaje
    /// de ese periodo que llegue después —también en una re-descarga— vaya a cuarentena con la corrección.
    /// </summary>
    public async Task RecordClockIncidentAsync(DeviceId deviceId, DateTime reportedFrom, DateTime reportedTo, TimeSpan behind, string reason, CancellationToken cancellationToken)
    {
        if (_incidents == null) return;
        var from = DateTime.SpecifyKind(reportedFrom, DateTimeKind.Unspecified);
        var to = DateTime.SpecifyKind(reportedTo, DateTimeKind.Unspecified);
        var existing = (await _incidents.GetForDeviceAsync(deviceId, cancellationToken))
            .FirstOrDefault(x => x.OffsetMinutes == Minutes(behind) && x.ReportedTo >= from.AddHours(-1));
        if (existing != null) existing.ExtendTo(to);
        else await _incidents.AddAsync(DeviceClockIncident.Create(deviceId, from, to, Minutes(behind), reason), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static DateTime Civil(PunchCandidate c) => DateTime.SpecifyKind(c.CivilTime, DateTimeKind.Unspecified);

    /// <summary>Corrección sugerida en minutos (redondeada al minuto).</summary>
    public static int Minutes(TimeSpan span) => (int)Math.Round(span.TotalMinutes);

    public static string Describe(TimeSpan span)
    {
        var minutes = Math.Abs(Minutes(span));
        if (minutes < 60) return $"{minutes} min";
        var days = minutes / 1440;
        var hours = minutes % 1440 / 60;
        var rest = minutes % 60;
        return string.Join(" ", new[] { days > 0 ? $"{days} d" : null, hours > 0 ? $"{hours} h" : null, rest > 0 ? $"{rest} min" : null }.Where(p => p != null));
    }
}
