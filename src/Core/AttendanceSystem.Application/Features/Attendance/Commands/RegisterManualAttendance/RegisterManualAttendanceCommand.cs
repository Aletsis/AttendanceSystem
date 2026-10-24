using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Services;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Features.Attendance.Decisions;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.Features.Attendance.Commands.RegisterManualAttendance;

/// <param name="CheckTime">Fecha y hora civil del marcaje en la zona horaria del empleado (no la fecha de la jornada).</param>
/// <param name="Reason">Caso 13: obligatorio; se guarda en el marcaje con quién y cuándo lo registró.</param>
[RequiresPermission(Permissions.AttendanceManualPunch)]
public sealed record RegisterManualAttendanceCommand(
    string EmployeeId,
    DateTime CheckTime,
    string? TimeZoneId = null,
    string? Reason = null) : IRequest<Result>;

/// <summary>
/// Captura manual de un marcaje (F03-03 / F03-05). El marcaje se trata igual que uno de dispositivo:
/// <list type="number">
/// <item>No lleva tipo: el recálculo decide su posición con las reglas normales, incluso si la jornada ya está calculada.</item>
/// <item>Se rechaza si el empleado ya tiene un marcaje en el mismo minuto.</item>
/// <item>La jornada a la que pertenece (incluida la previa cuando el turno cruza la medianoche) la decide el
/// recálculo a partir del instante civil, no la fecha capturada.</item>
/// </list>
/// Caso 13: exige motivo; el marcaje guarda quién lo registró, cuándo y por qué, y queda en el historial de la jornada.
/// El llamador recalcula la fecha civil del marcaje; el recálculo incluye por sí mismo la jornada previa que lo alcance.
/// </summary>
public sealed class RegisterManualAttendanceCommandHandler : IRequestHandler<RegisterManualAttendanceCommand, Result>
{
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly IPersistenceFailureClassifier _failureClassifier;
    private readonly ICurrentUserService? _currentUser;
    private readonly IAttendanceAdjustmentRepository? _history;

    public RegisterManualAttendanceCommandHandler(
        IAttendanceRepository attendanceRepo,
        IUnitOfWork unitOfWork,
        ITimeNormalizer timeNormalizer,
        IAuthoritativeTimeZoneResolver? timeZoneResolver = null,
        IPersistenceFailureClassifier? failureClassifier = null,
        ICurrentUserService? currentUser = null,
        IAttendanceAdjustmentRepository? history = null)
    {
        _currentUser = currentUser;
        _history = history;
        _attendanceRepo = attendanceRepo;
        _unitOfWork = unitOfWork;
        _timeNormalizer = timeNormalizer;
        _timeZoneResolver = timeZoneResolver ?? DefaultAuthoritativeTimeZoneResolver.Instance;
        _failureClassifier = failureClassifier ?? DefaultPersistenceFailureClassifier.Instance;
    }

    public async Task<Result> Handle(RegisterManualAttendanceCommand request, CancellationToken cancellationToken)
    {
        if (AdjustmentReasons.Validate(AdjustmentReasons.ManualPunch, request.Reason) is { } missingReason)
        {
            return Result.Failure(missingReason);
        }

        var employeeId = EmployeeId.From(request.EmployeeId);
        var authoritativeTimeZone = !string.IsNullOrWhiteSpace(request.TimeZoneId)
            ? request.TimeZoneId
            : await _timeZoneResolver.ResolveForEmployeeAsync(employeeId, cancellationToken);

        var civilCheckTime = TruncateToMinute(request.CheckTime);
        var utcCheckTime = _timeNormalizer.ToUtc(civilCheckTime, authoritativeTimeZone);
        var workDate = _timeNormalizer.ToWorkDate(utcCheckTime, authoritativeTimeZone);

        // Única restricción de captura: no puede existir otro marcaje del empleado en el mismo minuto.
        var nearby = await _attendanceRepo.GetByDateRangeAsync(workDate.AddDays(-1), workDate.AddDays(1), employeeId, cancellationToken);
        if (nearby.Any(r => TruncateToMinute(r.CheckTime) == utcCheckTime))
        {
            return Result.Failure($"El empleado {employeeId.Value} ya tiene un marcaje el {civilCheckTime:dd/MM/yyyy} a las {civilCheckTime:HH:mm}.");
        }

        // El tipo es sólo un valor inicial: el recálculo infiere la posición (SetInferredType).
        var record = AttendanceRecord.Create(
            employeeId,
            DeviceId.From("MANUAL"),
            utcCheckTime,
            VerifyMethod.Manual,
            CheckType.CheckIn,
            workDate: workDate,
            originalCheckTime: civilCheckTime,
            timeZoneId: authoritativeTimeZone,
            source: AttendanceRecord.ManualSource);
        var author = _currentUser is { IsAuthenticated: true, UserName: { Length: > 0 } name } ? name : "Sistema";
        record.RecordManualCapture(author, request.Reason!);

        var ingestion = new AttendanceRecordIngestion(_attendanceRepo, _unitOfWork, _failureClassifier);
        var outcome = await ingestion.IngestAsync(new[] { record }, cancellationToken);
        if (!outcome.IsSuccess)
        {
            return Result.Failure($"No se pudo registrar el marcaje manual ({outcome.Failure!.Kind}): {outcome.Failure.Message}");
        }
        if (outcome.Inserted.Count == 0)
        {
            // Otro proceso insertó el mismo instante entre la verificación y el guardado.
            return Result.Failure($"El empleado {employeeId.Value} ya tiene un marcaje el {civilCheckTime:dd/MM/yyyy} a las {civilCheckTime:HH:mm}.");
        }

        if (_history != null)
        {
            await _history.AddHistoryAsync(AttendanceAdjustmentHistory.Event(
                employeeId, workDate.ToDateTime(TimeOnly.MinValue), AdjustmentReasons.ManualPunch,
                "", $"marcaje manual {civilCheckTime:dd/MM/yyyy HH:mm}", record.Reason, author, record.RegisteredAt), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, value.Kind);
}
