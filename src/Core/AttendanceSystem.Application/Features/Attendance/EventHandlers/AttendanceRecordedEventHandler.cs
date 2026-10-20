using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Features.Attendance.EventHandlers;

/// <summary>
/// Convierte cada marcaje persistido en trabajo de consolidación durable (F04-01 / F04-03 / F04-04).
/// Se ejecuta desde el outbox, por lo que puede repetirse: registrar la solicitud es idempotente
/// (unión de rangos). Los fallos se propagan para que el outbox reintente; nunca se marca como
/// procesado un evento cuya solicitud no se guardó. No encola un trabajo por marcaje: sólo avisa al
/// consumidor, que agrupa por empleado.
/// </summary>
public sealed class AttendanceRecordedEventHandler
    : INotificationHandler<AttendanceRecordedEvent>
{
    private readonly IAttendanceConsolidationRepository _consolidationRepo;
    private readonly IConsolidationWakeup _wakeup;
    private readonly ILogger<AttendanceRecordedEventHandler> _logger;

    public AttendanceRecordedEventHandler(
        IAttendanceConsolidationRepository consolidationRepo,
        IConsolidationWakeup wakeup,
        ILogger<AttendanceRecordedEventHandler> logger)
    {
        _consolidationRepo = consolidationRepo;
        _wakeup = wakeup;
        _logger = logger;
    }

    public async Task Handle(
        AttendanceRecordedEvent notification,
        CancellationToken cancellationToken)
    {
        // La fecha laboral civil es la jornada afectada; el recálculo incluye por sí mismo la jornada
        // previa cuando su turno real alcanza el marcaje. Mensajes antiguos sin WorkDate usan el día UTC y el anterior.
        var (startDate, endDate) = notification.WorkDate is { } workDate
            ? (workDate.ToDateTime(TimeOnly.MinValue), workDate.ToDateTime(TimeOnly.MinValue))
            : (notification.CheckTime.Date.AddDays(-1), notification.CheckTime.Date);

        await _consolidationRepo.RequestAsync(notification.EmployeeId, startDate, endDate, cancellationToken);

        _logger.LogDebug(
            "Consolidación solicitada para {EmployeeId} ({Start:yyyy-MM-dd}..{End:yyyy-MM-dd}) por el marcaje {RecordId}.",
            notification.EmployeeId.Value, startDate, endDate, notification.RecordId.Value);

        _wakeup.Signal();
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
