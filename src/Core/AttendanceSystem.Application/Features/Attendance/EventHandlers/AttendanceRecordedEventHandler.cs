using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Attendance.EventHandlers;

public sealed class AttendanceRecordedEventHandler
    : INotificationHandler<AttendanceRecordedEvent>
{
    private readonly IEmailService _emailService;
    private readonly IAttendanceConsolidationRepository _consolidationRepo;
    private readonly IAttendanceJobScheduler _jobScheduler;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AttendanceRecordedEventHandler> _logger;

    public AttendanceRecordedEventHandler(
        IEmailService emailService,
        IAttendanceConsolidationRepository consolidationRepo,
        IAttendanceJobScheduler jobScheduler,
        IUnitOfWork unitOfWork,
        ILogger<AttendanceRecordedEventHandler> logger)
    {
        _emailService = emailService;
        _consolidationRepo = consolidationRepo;
        _jobScheduler = jobScheduler;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Handle(
        AttendanceRecordedEvent notification,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Procesando evento de marcaje: Empleado {EmployeeId} registró {CheckType} a las {CheckTime}",
            notification.EmployeeId,
            notification.CheckType.Name,
            notification.CheckTime);

        try
        {
            var employeeId = notification.EmployeeId;
            // Incluir el día anterior para turnos nocturnos o continuos que cruzan la medianoche
            var startDate = notification.CheckTime.Date.AddDays(-1);
            var endDate = notification.CheckTime.Date;

            var existingRequest = await _consolidationRepo.GetPendingByEmployeeAsync(employeeId, cancellationToken);
            if (existingRequest != null)
            {
                existingRequest.ExpandRange(startDate, endDate);
                _consolidationRepo.Update(existingRequest);
            }
            else
            {
                var newRequest = AttendanceConsolidationRequest.Create(employeeId, startDate, endDate);
                await _consolidationRepo.AddAsync(newRequest, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Encolar trabajo de cálculo para consolidar asistencia automáticamente (H27)
            _jobScheduler.EnqueueAttendanceProcessing(startDate, endDate, notification.EmployeeId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al registrar solicitud de consolidación para empleado {EmployeeId}", notification.EmployeeId);
        }
    }
}

public sealed class OutOfHoursCheckDetectedEventHandler
    : INotificationHandler<OutOfHoursCheckDetectedEvent>
{
    private readonly IEmailService _emailService;

    public OutOfHoursCheckDetectedEventHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public async Task Handle(
        OutOfHoursCheckDetectedEvent notification,
        CancellationToken cancellationToken)
    {
        // Enviar alerta a supervisores
        await _emailService.SendAlertAsync(
            subject: "Checada fuera de horario",
            body: $"Empleado {notification.EmployeeId} registró checada a las {notification.CheckTime}",
            level: AlertLevel.Late,
            cancellationToken: cancellationToken);
    }
}
