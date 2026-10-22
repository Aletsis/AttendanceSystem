using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AttendanceSystem.Application.Features.Devices.Quarantine;

/// <summary>Marcaje recibido de un reloj, con la hora civil que reportó y la zona en que se interpreta.</summary>
public sealed record PunchCandidate(string EmployeeId, DateTime CivilTime, string TimeZoneId, int VerifyMethodCode, int CheckTypeCode, string Source);

/// <summary>
/// Separa los marcajes con hora imposible (<see cref="PunchTimePlausibility"/>) antes de ingresarlos: se guardan en
/// cuarentena (una descarga repetida no los duplica) y queda una alerta del reloj. Los demás siguen su ingestión normal.
/// </summary>
public sealed class PunchQuarantineService
{
    private readonly IQuarantinedPunchRepository _quarantine;
    private readonly ISystemAlertRepository? _alerts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PunchQuarantineService> _logger;

    public PunchQuarantineService(
        IQuarantinedPunchRepository quarantine,
        IUnitOfWork unitOfWork,
        ITimeNormalizer timeNormalizer,
        ISystemAlertRepository? alerts = null,
        ILogger<PunchQuarantineService>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _quarantine = quarantine;
        _unitOfWork = unitOfWork;
        _timeNormalizer = timeNormalizer;
        _alerts = alerts;
        _logger = logger ?? NullLogger<PunchQuarantineService>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Hora civil actual de la zona.</summary>
    public DateTime ClockNow(string timeZoneId) =>
        DateTime.SpecifyKind(_timeNormalizer.ToCivilTime(_timeProvider.GetUtcNow().UtcDateTime, timeZoneId), DateTimeKind.Unspecified);

    /// <summary>Guarda en cuarentena los candidatos con hora imposible y devuelve sus posiciones (no deben ingresarse).</summary>
    public async Task<IReadOnlySet<int>> QuarantineImpossibleAsync(DeviceId deviceId, string deviceName, IReadOnlyList<PunchCandidate> candidates, CancellationToken cancellationToken = default)
    {
        var impossible = new Dictionary<int, string>();
        var nowByZone = new Dictionary<string, DateTime>();
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (!nowByZone.TryGetValue(c.TimeZoneId, out var now)) nowByZone[c.TimeZoneId] = now = ClockNow(c.TimeZoneId);
            var problem = PunchTimePlausibility.Problem(DateTime.SpecifyKind(c.CivilTime, DateTimeKind.Unspecified), now);
            if (problem != null) impossible[i] = problem;
        }
        if (impossible.Count == 0) return new HashSet<int>();

        var keys = impossible.Keys.Select(i => (candidates[i].EmployeeId, DateTime.SpecifyKind(candidates[i].CivilTime, DateTimeKind.Unspecified))).Distinct().ToList();
        var existing = await _quarantine.GetExistingAsync(deviceId, keys, cancellationToken);
        var added = new HashSet<(string, DateTime)>();
        var punches = new List<QuarantinedPunch>();
        foreach (var (i, reason) in impossible)
        {
            var c = candidates[i];
            var key = (c.EmployeeId, DateTime.SpecifyKind(c.CivilTime, DateTimeKind.Unspecified));
            if (existing.Contains(key) || !added.Add(key)) continue;
            punches.Add(QuarantinedPunch.Create(c.EmployeeId, deviceId, c.CivilTime, c.TimeZoneId, c.VerifyMethodCode, c.CheckTypeCode, c.Source, reason));
        }

        if (punches.Count > 0)
        {
            await _quarantine.AddRangeAsync(punches, cancellationToken);
            if (_alerts != null)
            {
                await _alerts.AddAsync(SystemAlert.Create(AlertType.PunchQuarantined, deviceId.Value,
                    $"El reloj {deviceName} envió {punches.Count} marcaje(s) con hora imposible; quedaron en cuarentena. {punches[0].Reason}"), cancellationToken);
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            AttendanceTelemetry.QuarantinedPunches.Add(punches.Count);
            _logger.LogWarning("ALERTA: {Count} marcaje(s) del reloj {Device} con hora imposible quedaron en cuarentena (p. ej. {Reason}).",
                punches.Count, deviceName, punches[0].Reason);
        }

        return impossible.Keys.ToHashSet();
    }
}
