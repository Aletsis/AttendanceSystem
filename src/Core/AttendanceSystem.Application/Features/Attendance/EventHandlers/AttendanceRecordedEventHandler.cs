namespace AttendanceSystem.Application.Features.Attendance.EventHandlers;

public sealed class AttendanceRecordedEventHandler 
    : INotificationHandler<AttendanceRecordedEvent>
{
    private readonly IEmailService _emailService;
    private readonly ILogger<AttendanceRecordedEventHandler> _logger;

    public AttendanceRecordedEventHandler(
        IEmailService emailService,
        ILogger<AttendanceRecordedEventHandler> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task Handle(
        AttendanceRecordedEvent notification, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Procesando evento: Empleado {EmployeeId} registró {CheckType} a las {CheckTime}",
            notification.EmployeeId,
            notification.CheckType.Name,
            notification.CheckTime);

        // Lógica de aplicación: notificar, actualizar reportes, etc.
        // NO es lógica de negocio (esa va en el dominio)
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
            cancellationToken);
    }
}