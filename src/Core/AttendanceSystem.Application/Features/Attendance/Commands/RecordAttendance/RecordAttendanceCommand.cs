namespace AttendanceSystem.Application.Features.Attendance.Commands.RecordAttendance;

public sealed record RecordAttendanceCommand(
    string EmployeeId,
    string DeviceId,
    DateTime CheckTime,
    int VerifyMethodCode,
    int CheckTypeCode) : IRequest<Result<Guid>>;

// Handler
public sealed class RecordAttendanceCommandHandler 
    : IRequestHandler<RecordAttendanceCommand, Result<Guid>>
{
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public RecordAttendanceCommandHandler(
        IAttendanceRepository attendanceRepository,
        IUnitOfWork unitOfWork,
        IPublisher publisher)
    {
        _attendanceRepository = attendanceRepository;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<Result<Guid>> Handle(
        RecordAttendanceCommand command, 
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Crear Value Objects
            var employeeId = EmployeeId.From(command.EmployeeId);
            var deviceId = DeviceId.From(command.DeviceId);
            var verifyMethod = VerifyMethod.FromValue(command.VerifyMethodCode);
            var checkType = CheckType.FromValue(command.CheckTypeCode);

            // 2. Crear el agregado (contiene lógica de negocio)
            var record = AttendanceRecord.Create(
                employeeId,
                deviceId,
                command.CheckTime,
                verifyMethod,
                checkType);

            // 3. Persistir
            await _attendanceRepository.AddAsync(record, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 4. Publicar eventos de dominio
            foreach (var domainEvent in record.DomainEvents)
            {
                await _publisher.Publish(domainEvent, cancellationToken);
            }

            record.ClearDomainEvents();

            return Result<Guid>.Success(record.Id.Value);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }
    }
}