using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Services;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AttendanceSystem.Application.Features.Devices.Quarantine;

public sealed record PunchCorrectionRowDto(
    Guid RecordId, string EmployeeId, DateTime OldCivil, DateOnly OldWorkDate, string? OldTimeZone,
    DateTime NewCivil, DateOnly NewWorkDate, string NewTimeZone, string? SkipReason);

public sealed record PunchCorrectionResultDto(bool Applied, Guid? CorrectionId, IReadOnlyList<PunchCorrectionRowDto> Rows)
{
    public int Corrected => Rows.Count(r => r.SkipReason == null);
    public int Skipped => Rows.Count(r => r.SkipReason != null);
}

/// <summary>
/// Reinterpreta los marcajes de un reloj cuya fecha laboral cae en [FromDate, ToDate]: con la zona correcta
/// (<paramref name="TimeZoneId"/>; la hora que marcó el reloj se conserva) y/o con un desfase en minutos (el reloj
/// marcaba una hora equivocada). Con <paramref name="Apply"/> = false sólo muestra la vista previa.
/// </summary>
[RequiresPermission(Permissions.DevicesManage)]
public sealed record CorrectDevicePunchTimesCommand(
    string DeviceId, DateOnly FromDate, DateOnly ToDate, string? TimeZoneId, int OffsetMinutes, string Reason, bool Apply)
    : IRequest<Result<PunchCorrectionResultDto>>;

public sealed class CorrectDevicePunchTimesCommandHandler : IRequestHandler<CorrectDevicePunchTimesCommand, Result<PunchCorrectionResultDto>>
{
    public const int MaxDays = 93;
    public const int MaxOffsetMinutes = 7 * 24 * 60;

    private readonly IDeviceRepository _devices;
    private readonly IPunchTimeCorrectionRepository _corrections;
    private readonly IAttendanceRepository _attendance;
    private readonly IPunchAssignmentRepository _assignments;
    private readonly IAttendanceConsolidationRepository _consolidations;
    private readonly IConsolidationWakeup _wakeup;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceCalculationLockService _lockService;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly ICurrentUserService? _currentUser;
    private readonly IAttendanceAdjustmentRepository? _adjustments;
    private readonly ILogger<CorrectDevicePunchTimesCommandHandler> _logger;

    public CorrectDevicePunchTimesCommandHandler(
        IDeviceRepository devices,
        IPunchTimeCorrectionRepository corrections,
        IAttendanceRepository attendance,
        IPunchAssignmentRepository assignments,
        IAttendanceConsolidationRepository consolidations,
        IConsolidationWakeup wakeup,
        IUnitOfWork unitOfWork,
        IAttendanceCalculationLockService lockService,
        ITimeNormalizer timeNormalizer,
        IAuthoritativeTimeZoneResolver timeZoneResolver,
        ICurrentUserService? currentUser = null,
        ILogger<CorrectDevicePunchTimesCommandHandler>? logger = null,
        IAttendanceAdjustmentRepository? adjustments = null)
    {
        _adjustments = adjustments;
        _devices = devices;
        _corrections = corrections;
        _attendance = attendance;
        _assignments = assignments;
        _consolidations = consolidations;
        _wakeup = wakeup;
        _unitOfWork = unitOfWork;
        _lockService = lockService;
        _timeNormalizer = timeNormalizer;
        _timeZoneResolver = timeZoneResolver;
        _currentUser = currentUser;
        _logger = logger ?? NullLogger<CorrectDevicePunchTimesCommandHandler>.Instance;
    }

    public async Task<Result<PunchCorrectionResultDto>> Handle(CorrectDevicePunchTimesCommand request, CancellationToken cancellationToken)
    {
        if (request.ToDate < request.FromDate) return Result<PunchCorrectionResultDto>.Failure("El rango de fechas es inválido.");
        if (request.ToDate.DayNumber - request.FromDate.DayNumber + 1 > MaxDays)
            return Result<PunchCorrectionResultDto>.Failure($"El rango no puede exceder {MaxDays} días; corrija por partes.");
        if (Math.Abs(request.OffsetMinutes) > MaxOffsetMinutes)
            return Result<PunchCorrectionResultDto>.Failure("El desfase no puede exceder 7 días.");
        var zone = string.IsNullOrWhiteSpace(request.TimeZoneId) ? null : request.TimeZoneId.Trim();
        if (zone != null && !TimeZoneCatalog.IsValid(zone)) return Result<PunchCorrectionResultDto>.Failure($"Zona horaria desconocida: {zone}");
        if (zone == null && request.OffsetMinutes == 0) return Result<PunchCorrectionResultDto>.Failure("Indique la zona correcta o un desfase.");
        if (request.Apply && string.IsNullOrWhiteSpace(request.Reason)) return Result<PunchCorrectionResultDto>.Failure("Indique el motivo de la corrección.");

        var deviceId = DeviceId.From(request.DeviceId);
        if (await _devices.GetByIdAsync(deviceId, cancellationToken) == null) return Result<PunchCorrectionResultDto>.Failure("Dispositivo no encontrado.");
        var deviceZone = await _timeZoneResolver.ResolveForDeviceAsync(deviceId, cancellationToken);

        if (!request.Apply)
        {
            var records = await _corrections.GetDeviceRecordsAsync(deviceId, request.FromDate, request.ToDate, cancellationToken);
            var preview = await PlanAsync(records, zone, request.OffsetMinutes, deviceZone, cancellationToken);
            return Result<PunchCorrectionResultDto>.Success(new PunchCorrectionResultDto(false, null, preview.Select(p => p.Row).ToList()));
        }

        var author = _currentUser is { IsAuthenticated: true, UserName: { Length: > 0 } name } ? name : "Sistema";
        PunchCorrectionResultDto? result = null;
        List<(EmployeeId, DateTime, DateTime)> ranges = new();

        // Empleados con marcajes en el rango: su recálculo queda excluido mientras se corrigen.
        var employees = (await _corrections.GetDeviceRecordsAsync(deviceId, request.FromDate, request.ToDate, cancellationToken))
            .Select(r => r.EmployeeId).Distinct().ToList();
        if (employees.Count == 0)
            return Result<PunchCorrectionResultDto>.Success(new PunchCorrectionResultDto(false, null, Array.Empty<PunchCorrectionRowDto>()));

        var written = await ExclusiveAttendanceWrite.RunAsync(_unitOfWork, _lockService, employees, async () =>
        {
            var records = await _corrections.GetDeviceRecordsAsync(deviceId, request.FromDate, request.ToDate, cancellationToken);
            var plan = await PlanAsync(records, zone, request.OffsetMinutes, deviceZone, cancellationToken);
            var moving = plan.Where(p => p.Row.SkipReason == null).ToList();
            if (moving.Count == 0) return Result.Failure("Ningún marcaje cambia con esta corrección.");

            var correction = PunchTimeCorrection.Create(deviceId, request.FromDate, request.ToDate, zone, request.OffsetMinutes, request.Reason, author);
            foreach (var skipped in plan.Where(p => p.Row.SkipReason != null)) correction.Skip();

            // La asignación de un marcaje corregido ya no vale (puede cambiar de jornada): se rehace al recalcular.
            var assignments = await _assignments.GetByRecordIdsAsync(moving.Select(m => m.Record.Id).ToList(), cancellationToken);
            foreach (var assignment in assignments) _assignments.Remove(assignment);

            // Caso 14: el original no se modifica; se anula y lo reemplaza un marcaje corregido.
            var replacements = moving
                .Select(m => (Original: m.Record, Corrected: correction.Apply(m.Record, m.NewUtc, m.Row.NewCivil, m.Row.NewWorkDate, m.Row.NewTimeZone)))
                .ToList();
            await _corrections.AddAsync(correction, cancellationToken);
            await RepointDecisionsAsync(replacements, author, cancellationToken);

            // La identidad (empleado, instante) es única entre los marcajes vigentes y un corregido puede ocupar el instante
            // de otro original: primero se anulan los originales y después se insertan los corregidos.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _attendance.AddRangeAsync(replacements.Select(r => r.Corrected).ToList(), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            ranges = moving
                .GroupBy(m => m.Record.EmployeeId)
                .Select(g =>
                {
                    var dates = g.SelectMany(m => new[] { m.Row.OldWorkDate, m.Row.NewWorkDate }).ToList();
                    return (g.Key, dates.Min().ToDateTime(TimeOnly.MinValue), dates.Max().ToDateTime(TimeOnly.MinValue));
                })
                .ToList();
            await _consolidations.RequestRangesAsync(ranges, cancellationToken);

            result = new PunchCorrectionResultDto(true, correction.Id, plan.Select(p => p.Row).ToList());
            return Result.Success();
        }, cancellationToken);

        if (written.IsFailure) return Result<PunchCorrectionResultDto>.Failure(written.Error);

        _wakeup.Signal();
        _logger.LogWarning("Corrección de hora del reloj {Device} ({From}–{To}, zona {Zone}, desfase {Offset} min) por {Author}: {Corrected} marcajes, {Skipped} omitidos. Motivo: {Reason}",
            request.DeviceId, request.FromDate, request.ToDate, zone ?? "(sin cambio)", request.OffsetMinutes, author, result!.Corrected, result.Skipped, request.Reason);
        return Result<PunchCorrectionResultDto>.Success(result);
    }

    private sealed record Planned(AttendanceRecord Record, DateTime NewUtc, PunchCorrectionRowDto Row);

    /// <summary>Las decisiones de RH que usaban un marcaje corregido pasan a su reemplazo.</summary>
    private async Task RepointDecisionsAsync(IReadOnlyList<(AttendanceRecord Original, AttendanceRecord Corrected)> replacements, string author, CancellationToken cancellationToken)
    {
        if (_adjustments == null) return;
        foreach (var group in replacements.GroupBy(r => r.Original.EmployeeId))
        {
            var dates = group.SelectMany(r => new[] { r.Original.GetWorkDate(), r.Corrected.GetWorkDate() }).ToList();
            var from = dates.Min().AddDays(-1).ToDateTime(TimeOnly.MinValue);
            var to = dates.Max().AddDays(1).ToDateTime(TimeOnly.MinValue);
            foreach (var adjustment in await _adjustments.GetByDateRangeAsync(from, to, employeeId: group.Key, cancellationToken: cancellationToken))
            {
                var changed = false;
                foreach (var (original, corrected) in group)
                {
                    changed |= adjustment.ReplaceRecord(original.Id, corrected.Id, corrected.CheckTime, author);
                }
                if (changed) _adjustments.Update(adjustment);
            }
        }
    }

    /// <summary>
    /// Nueva hora de cada marcaje. Se omiten (con motivo) los que no cambian y los que chocarían con otro marcaje del
    /// mismo empleado en el instante corregido.
    /// </summary>
    private async Task<List<Planned>> PlanAsync(IReadOnlyList<AttendanceRecord> records, string? zone, int offsetMinutes, string deviceZone, CancellationToken cancellationToken)
    {
        var drafts = records.Select(r =>
        {
            var oldCivil = DateTime.SpecifyKind(r.GetCivilTime(r.TimeZoneId ?? deviceZone), DateTimeKind.Unspecified);
            var newZone = zone ?? r.TimeZoneId ?? deviceZone;
            var newCivil = oldCivil.AddMinutes(offsetMinutes);
            var newUtc = _timeNormalizer.ToUtc(newCivil, newZone);
            var newWorkDate = _timeNormalizer.ToWorkDate(newUtc, newZone);
            var unchanged = newUtc == r.CheckTime && newZone == r.TimeZoneId && newWorkDate == r.WorkDate;
            return (Record: r, OldCivil: oldCivil, NewZone: newZone, NewCivil: newCivil, NewUtc: newUtc, NewWorkDate: newWorkDate, Unchanged: unchanged);
        }).ToList();

        var movingIds = drafts.Where(d => !d.Unchanged).Select(d => d.Record.Id).ToHashSet();
        var wanted = drafts.Where(d => !d.Unchanged).Select(d => (d.Record.EmployeeId, d.NewUtc)).Distinct().ToList();
        var occupied = (await _attendance.GetByIdentitiesAsync(wanted, cancellationToken))
            .Where(r => !movingIds.Contains(r.Id) && !r.IsVoided)
            .Select(r => (r.EmployeeId, r.CheckTime))
            .ToHashSet();

        var claimed = new HashSet<(EmployeeId, DateTime)>();
        return drafts.Select(d =>
        {
            string? skip = null;
            if (d.Unchanged) skip = "Sin cambio";
            else if (occupied.Contains((d.Record.EmployeeId, d.NewUtc)) || !claimed.Add((d.Record.EmployeeId, d.NewUtc)))
                skip = "Ya existe un marcaje del empleado a esa hora";
            var row = new PunchCorrectionRowDto(d.Record.Id.Value, d.Record.EmployeeId.Value, d.OldCivil, d.Record.GetWorkDate(deviceZone), d.Record.TimeZoneId,
                d.NewCivil, d.NewWorkDate, d.NewZone, skip);
            return new Planned(d.Record, d.NewUtc, row);
        }).ToList();
    }
}

/// <summary>Libera un marcaje en cuarentena con su hora corregida (reportada + desfase): se ingresa como cualquier otro.</summary>
[RequiresPermission(Permissions.DevicesManage)]
public sealed record ReleaseQuarantinedPunchCommand(Guid Id, int OffsetMinutes, string Note) : IRequest<Result<string>>;

/// <summary>Descarta un marcaje en cuarentena (no corresponde a un marcaje real).</summary>
[RequiresPermission(Permissions.DevicesManage)]
public sealed record DiscardQuarantinedPunchCommand(Guid Id, string Note) : IRequest<Result>;

public sealed class QuarantinedPunchDecisionHandler :
    IRequestHandler<ReleaseQuarantinedPunchCommand, Result<string>>,
    IRequestHandler<DiscardQuarantinedPunchCommand, Result>
{
    private readonly IQuarantinedPunchRepository _quarantine;
    private readonly IAttendanceRepository _attendance;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly PunchQuarantineService _service;
    private readonly IPersistenceFailureClassifier _classifier;
    private readonly ICurrentUserService? _currentUser;
    private readonly ILogger<QuarantinedPunchDecisionHandler> _logger;

    public QuarantinedPunchDecisionHandler(
        IQuarantinedPunchRepository quarantine,
        IAttendanceRepository attendance,
        IUnitOfWork unitOfWork,
        ITimeNormalizer timeNormalizer,
        PunchQuarantineService service,
        IPersistenceFailureClassifier? classifier = null,
        ICurrentUserService? currentUser = null,
        ILogger<QuarantinedPunchDecisionHandler>? logger = null)
    {
        _quarantine = quarantine;
        _attendance = attendance;
        _unitOfWork = unitOfWork;
        _timeNormalizer = timeNormalizer;
        _service = service;
        _classifier = classifier ?? DefaultPersistenceFailureClassifier.Instance;
        _currentUser = currentUser;
        _logger = logger ?? NullLogger<QuarantinedPunchDecisionHandler>.Instance;
    }

    private string Author => _currentUser is { IsAuthenticated: true, UserName: { Length: > 0 } name } ? name : "Sistema";

    public async Task<Result<string>> Handle(ReleaseQuarantinedPunchCommand request, CancellationToken cancellationToken)
    {
        var punch = await _quarantine.GetByIdAsync(request.Id, cancellationToken);
        if (punch == null) return Result<string>.Failure("Marcaje en cuarentena no encontrado.");
        if (punch.Status != QuarantineStatus.Pending) return Result<string>.Failure("El marcaje ya fue resuelto.");
        if (string.IsNullOrWhiteSpace(request.Note)) return Result<string>.Failure("Indique el motivo de la decisión.");

        var corrected = DateTime.SpecifyKind(punch.ReportedTime, DateTimeKind.Unspecified).AddMinutes(request.OffsetMinutes);
        var problem = PunchTimePlausibility.Problem(corrected, _service.ClockNow(punch.TimeZoneId));
        if (problem != null) return Result<string>.Failure($"La hora corregida sigue siendo imposible: {problem}");

        try
        {
            var utc = _timeNormalizer.ToUtc(corrected, punch.TimeZoneId);
            var record = AttendanceRecord.Create(EmployeeId.From(punch.EmployeeId), punch.DeviceId, utc,
                VerifyMethod.FromValue(punch.VerifyMethodCode), CheckType.FromValue(punch.CheckTypeCode),
                workDate: _timeNormalizer.ToWorkDate(utc, punch.TimeZoneId), originalCheckTime: corrected,
                timeZoneId: punch.TimeZoneId, source: punch.Source);

            var outcome = await new AttendanceRecordIngestion(_attendance, _unitOfWork, _classifier, _logger).IngestAsync(new[] { record }, cancellationToken);
            if (!outcome.IsSuccess) return Result<string>.Failure($"No se pudo ingresar el marcaje: {outcome.Failure!.Message}");

            string message;
            if (outcome.Inserted.Count == 1)
            {
                punch.Release(corrected, record.Id, Author, request.Note);
                message = $"Marcaje ingresado a las {corrected:dd/MM/yyyy HH:mm}; su jornada se recalculará.";
            }
            else
            {
                punch.ReleaseAsDuplicate(corrected, Author, $"{request.Note.Trim()} (ya existía un marcaje a esa hora)");
                message = "Ya existía un marcaje del empleado a esa hora; se cerró sin duplicarlo.";
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Marcaje en cuarentena {Id} liberado por {Author} a {Time:yyyy-MM-dd HH:mm}.", punch.Id, Author, corrected);
            return Result<string>.Success(message);
        }
        catch (DomainException ex)
        {
            return Result<string>.Failure(ex.Message);
        }
    }

    public async Task<Result> Handle(DiscardQuarantinedPunchCommand request, CancellationToken cancellationToken)
    {
        var punch = await _quarantine.GetByIdAsync(request.Id, cancellationToken);
        if (punch == null) return Result.Failure("Marcaje en cuarentena no encontrado.");
        try
        {
            punch.Discard(Author, request.Note);
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Marcaje en cuarentena {Id} descartado por {Author}: {Note}", punch.Id, Author, request.Note);
        return Result.Success();
    }
}
