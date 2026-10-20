using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Features.Attendance.Commands.ManuallyAssignAttendance;
using AttendanceSystem.Application.Features.Attendance.Services;
using MediatR;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.Features.Attendance.Commands.RegisterManualAttendance;

public sealed record RegisterManualAttendanceCommand(
    string EmployeeId,
    DateTime CheckTime,
    string Type,
    string? TimeZoneId = null) : IRequest<Result>;

/// <summary>
/// Captura manual de un marcaje (F03-03 / F03-05):
/// 1. Inserta el marcaje con la identidad única (empleado, instante); si ya existe se reutiliza.
/// 2. Si la jornada ya está calculada, lo asigna como decisión manual con <see cref="ManuallyAssignAttendanceCommand"/>,
///    que valida pertenencia, mantiene las filas de asignación y se coordina con el recálculo mediante el bloqueo del empleado.
/// Si la jornada no existe, el recálculo posterior (consolidación del evento de marcaje) lo considerará.
/// </summary>
public sealed class RegisterManualAttendanceCommandHandler : IRequestHandler<RegisterManualAttendanceCommand, Result>
{
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly IDailyAttendanceRepository _dailyRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly ISender _sender;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly IPersistenceFailureClassifier _failureClassifier;

    public RegisterManualAttendanceCommandHandler(
        IAttendanceRepository attendanceRepo,
        IDailyAttendanceRepository dailyRepo,
        IUnitOfWork unitOfWork,
        ITimeNormalizer timeNormalizer,
        ISender sender,
        IAuthoritativeTimeZoneResolver? timeZoneResolver = null,
        IPersistenceFailureClassifier? failureClassifier = null)
    {
        _attendanceRepo = attendanceRepo;
        _dailyRepo = dailyRepo;
        _unitOfWork = unitOfWork;
        _timeNormalizer = timeNormalizer;
        _sender = sender;
        _timeZoneResolver = timeZoneResolver ?? DefaultAuthoritativeTimeZoneResolver.Instance;
        _failureClassifier = failureClassifier ?? DefaultPersistenceFailureClassifier.Instance;
    }

    public async Task<Result> Handle(RegisterManualAttendanceCommand request, CancellationToken cancellationToken)
    {
        if (request.Type != "Entrada" && request.Type != "Salida")
        {
            return Result.Failure("Tipo de marcaje manual inválido. Use 'Entrada' o 'Salida'.");
        }

        var employeeId = EmployeeId.From(request.EmployeeId);
        var authoritativeTimeZone = !string.IsNullOrWhiteSpace(request.TimeZoneId)
            ? request.TimeZoneId
            : await _timeZoneResolver.ResolveForEmployeeAsync(employeeId, cancellationToken);

        var utcCheckTime = _timeNormalizer.ToUtc(request.CheckTime, authoritativeTimeZone);
        var workDate = _timeNormalizer.ToWorkDate(utcCheckTime, authoritativeTimeZone);
        var date = workDate.ToDateTime(TimeOnly.MinValue);

        var record = AttendanceRecord.Create(
            employeeId,
            DeviceId.From("MANUAL"),
            utcCheckTime,
            VerifyMethod.Manual,
            request.Type == "Entrada" ? CheckType.CheckIn : CheckType.CheckOut,
            workDate: workDate,
            originalCheckTime: request.CheckTime,
            timeZoneId: authoritativeTimeZone,
            source: "Manual");

        var ingestion = new AttendanceRecordIngestion(_attendanceRepo, _unitOfWork, _failureClassifier);
        var outcome = await ingestion.IngestAsync(new[] { record }, cancellationToken);
        if (!outcome.IsSuccess)
        {
            return Result.Failure($"No se pudo registrar el marcaje manual ({outcome.Failure!.Kind}): {outcome.Failure.Message}");
        }

        var recordId = outcome.IdFor(employeeId, utcCheckTime)!;

        var daily = await _dailyRepo.GetByEmployeeAndDateAsync(employeeId, date, cancellationToken);
        if (daily == null)
        {
            return Result.Success();
        }

        return await _sender.Send(new ManuallyAssignAttendanceCommand(
            employeeId.Value,
            workDate,
            recordId.Value.ToString(),
            request.Type), cancellationToken);
    }
}
