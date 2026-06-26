using System.Collections.Generic;

namespace AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;

public sealed record AttendanceLogDto(
    string EmployeeId,
    DateTime CheckTime,
    int VerifyMethodCode,
    int CheckTypeCode
);

public sealed record RecordAttendanceBatchCommand(
    string DeviceId,
    List<AttendanceLogDto> Logs
) : IRequest<Result<int>>;

// Handler
public sealed class RecordAttendanceBatchCommandHandler 
    : IRequestHandler<RecordAttendanceBatchCommand, Result<int>>
{
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;
    private readonly ILogger<RecordAttendanceBatchCommandHandler> _logger;

    public RecordAttendanceBatchCommandHandler(
        IAttendanceRepository attendanceRepository,
        IUnitOfWork unitOfWork,
        IPublisher publisher,
        ILogger<RecordAttendanceBatchCommandHandler> logger)
    {
        _attendanceRepository = attendanceRepository;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(
        RecordAttendanceBatchCommand command, 
        CancellationToken cancellationToken)
    {
        if (command.Logs == null || command.Logs.Count == 0)
        {
            return Result<int>.Success(0);
        }

        try
        {
            var deviceId = DeviceId.From(command.DeviceId);
            var minDate = command.Logs.Min(l => l.CheckTime);
            var maxDate = command.Logs.Max(l => l.CheckTime);

            // Fetch existing logs in the batch's time range for this device to prevent duplicates
            var existingRecords = await _attendanceRepository.GetByDeviceAndDateRangeAsync(
                deviceId,
                minDate,
                maxDate,
                cancellationToken);

            var existingKeys = existingRecords
                .Select(r => (r.EmployeeId.Value, r.CheckTime))
                .ToHashSet();

            var newRecords = new List<AttendanceRecord>();
            var processedKeys = new HashSet<(string, DateTime)>();
            int skippedDuplicates = 0;

            foreach (var log in command.Logs)
            {
                var employeeIdVal = log.EmployeeId;
                var checkTime = log.CheckTime;

                // Skip if duplicate in the database or already seen in the current batch
                if (existingKeys.Contains((employeeIdVal, checkTime)) || 
                    processedKeys.Contains((employeeIdVal, checkTime)))
                {
                    skippedDuplicates++;
                    continue;
                }

                processedKeys.Add((employeeIdVal, checkTime));

                var employeeId = EmployeeId.From(employeeIdVal);
                var verifyMethod = VerifyMethod.FromValue(log.VerifyMethodCode);
                var checkType = CheckType.FromValue(log.CheckTypeCode);

                var record = AttendanceRecord.Create(
                    employeeId,
                    deviceId,
                    checkTime,
                    verifyMethod,
                    checkType);

                newRecords.Add(record);
            }

            if (newRecords.Count > 0)
            {
                await _attendanceRepository.AddRangeAsync(newRecords, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Publish domain events for all newly created records
                foreach (var record in newRecords)
                {
                    foreach (var domainEvent in record.DomainEvents)
                    {
                        await _publisher.Publish(domainEvent, cancellationToken);
                    }
                    record.ClearDomainEvents();
                }
            }

            _logger.LogInformation("Procesamiento por lotes completado para dispositivo {DeviceId}. " +
                                  "Agregados: {AddedCount}, Duplicados omitidos: {SkippedCount}", 
                                  command.DeviceId, newRecords.Count, skippedDuplicates);

            return Result<int>.Success(newRecords.Count);
        }
        catch (DomainException ex)
        {
            return Result<int>.Failure(ex.Message);
        }
    }
}
