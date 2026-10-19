using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;

public sealed record RecordAttendanceCommand(
    string EmployeeId,
    string DeviceId,
    DateTime CheckTime,
    int VerifyMethodCode,
    int CheckTypeCode,
    string? TimeZoneId = null,
    string? Source = null) : IRequest<Result<Guid>>;

// Handler
public sealed class RecordAttendanceCommandHandler
    : IRequestHandler<RecordAttendanceCommand, Result<Guid>>
{
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly ILogger<RecordAttendanceCommandHandler> _logger;

    public RecordAttendanceCommandHandler(
        IAttendanceRepository attendanceRepository,
        IUnitOfWork unitOfWork,
        ITimeNormalizer timeNormalizer,
        ILogger<RecordAttendanceCommandHandler>? logger)
        : this(attendanceRepository, unitOfWork, timeNormalizer, DefaultAuthoritativeTimeZoneResolver.Instance, logger)
    {
    }

    public RecordAttendanceCommandHandler(
        IAttendanceRepository attendanceRepository,
        IUnitOfWork unitOfWork,
        ITimeNormalizer timeNormalizer,
        IAuthoritativeTimeZoneResolver? timeZoneResolver,
        ILogger<RecordAttendanceCommandHandler>? logger = null)
    {
        _attendanceRepository = attendanceRepository;
        _unitOfWork = unitOfWork;
        _timeNormalizer = timeNormalizer;
        _timeZoneResolver = timeZoneResolver ?? DefaultAuthoritativeTimeZoneResolver.Instance;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RecordAttendanceCommandHandler>.Instance;
    }

    public async Task<Result<Guid>> Handle(
        RecordAttendanceCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Crear Value Objects y normalizar tiempo a UTC
            var employeeId = EmployeeId.From(command.EmployeeId);
            var deviceId = DeviceId.From(command.DeviceId);
            var verifyMethod = VerifyMethod.FromValue(command.VerifyMethodCode);
            var checkType = CheckType.FromValue(command.CheckTypeCode);

            var tz = !string.IsNullOrWhiteSpace(command.TimeZoneId)
                ? command.TimeZoneId
                : await _timeZoneResolver.ResolveForDeviceAsync(deviceId, cancellationToken);

            var utcCheckTime = _timeNormalizer.ToUtc(command.CheckTime, tz);
            var workDate = _timeNormalizer.ToWorkDate(utcCheckTime, tz);
            var originalCheckTime = command.CheckTime.Kind == DateTimeKind.Utc
                ? _timeNormalizer.ToCivilTime(command.CheckTime, tz)
                : command.CheckTime;

            // 2. Comprobar si ya existe el registro (Idempotencia preventiva)
            var existingRecord = await _attendanceRepository.GetByEmployeeAndCheckTimeAsync(
                employeeId,
                utcCheckTime,
                cancellationToken);

            if (existingRecord != null)
            {
                _logger.LogInformation("Marcaje existente detectado para empleado {EmployeeId} a las {CheckTime} UTC. Retornando ID existente {Id}.",
                    command.EmployeeId, utcCheckTime, existingRecord.Id.Value);
                return Result<Guid>.Success(existingRecord.Id.Value);
            }

            // 3. Crear el agregado (contiene lógica de negocio)
            var record = AttendanceRecord.Create(
                employeeId,
                deviceId,
                utcCheckTime,
                verifyMethod,
                checkType,
                workDate: workDate,
                originalCheckTime: originalCheckTime,
                timeZoneId: tz,
                source: command.Source ?? "API");

            // 4. Persistir con manejo de conflicto esperado de concurrencia
            try
            {
                await _attendanceRepository.AddAsync(record, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Posible carrera de concurrencia al insertar marcaje para empleado {EmployeeId} a las {CheckTime} UTC.",
                    command.EmployeeId, utcCheckTime);

                var concurrentRecord = await _attendanceRepository.GetByEmployeeAndCheckTimeAsync(
                    employeeId,
                    utcCheckTime,
                    cancellationToken);

                if (concurrentRecord != null)
                {
                    return Result<Guid>.Success(concurrentRecord.Id.Value);
                }

                throw;
            }

            return Result<Guid>.Success(record.Id.Value);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }
    }
}
