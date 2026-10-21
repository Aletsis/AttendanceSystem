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
    private readonly IConsolidationRequestTracker? _tracker;

    public AttendanceRecordedEventHandler(
        IAttendanceConsolidationRepository consolidationRepo,
        IConsolidationWakeup wakeup,
        ILogger<AttendanceRecordedEventHandler> logger,
        IConsolidationRequestTracker? tracker = null)
    {
        _consolidationRepo = consolidationRepo;
        _wakeup = wakeup;
        _logger = logger;
        _tracker = tracker;
    }

    /// <summary>Jornadas afectadas por un marcaje (fecha laboral; mensajes antiguos sin ella: día UTC y el anterior).</summary>
    public static (DateTime Start, DateTime End) AffectedRange(AttendanceRecordedEvent notification) =>
        notification.WorkDate is { } workDate
            ? (workDate.ToDateTime(TimeOnly.MinValue), workDate.ToDateTime(TimeOnly.MinValue))
            : (notification.CheckTime.Date.AddDays(-1), notification.CheckTime.Date);

    public async Task Handle(
        AttendanceRecordedEvent notification,
        CancellationToken cancellationToken)
    {
        // La fecha laboral civil es la jornada afectada; el recálculo incluye por sí mismo la jornada
        // previa cuando su turno real alcanza el marcaje. Mensajes antiguos sin WorkDate usan el día UTC y el anterior.
        var (startDate, endDate) = AffectedRange(notification);

        // F07-04 / C01: si el lote del mismo commit ya registró la solicitud, no se repite por marcaje.
        if (_tracker?.IsCovered(notification.EmployeeId, startDate, endDate) == true)
        {
            _wakeup.Signal();
            return;
        }

        await _consolidationRepo.RequestAsync(notification.EmployeeId, startDate, endDate, cancellationToken);
        _tracker?.MarkRequested(notification.EmployeeId, startDate, endDate);

        _logger.LogDebug(
            "Consolidación solicitada para {EmployeeId} ({Start:yyyy-MM-dd}..{End:yyyy-MM-dd}) por el marcaje {RecordId}.",
            notification.EmployeeId.Value, startDate, endDate, notification.RecordId.Value);

        _wakeup.Signal();
    }
}

/// <summary>
/// F07-04 / C01: los marcajes de un mismo commit, publicados juntos por el outbox. Su manejador registra las
/// solicitudes de consolidación de todo el lote en una sentencia (un rango por empleado); los eventos individuales
/// que siguen sólo avisan al consumidor.
/// </summary>
public sealed record AttendanceRecordedBatch(IReadOnlyList<AttendanceRecordedEvent> Events) : INotification;

public sealed class AttendanceRecordedBatchHandler : INotificationHandler<AttendanceRecordedBatch>
{
    private readonly IAttendanceConsolidationRepository _consolidationRepo;
    private readonly IConsolidationRequestTracker _tracker;

    public AttendanceRecordedBatchHandler(IAttendanceConsolidationRepository consolidationRepo, IConsolidationRequestTracker tracker)
    {
        _consolidationRepo = consolidationRepo;
        _tracker = tracker;
    }

    public async Task Handle(AttendanceRecordedBatch notification, CancellationToken cancellationToken)
    {
        var ranges = notification.Events
            .Select(e => (e.EmployeeId, Range: AttendanceRecordedEventHandler.AffectedRange(e)))
            .GroupBy(x => x.EmployeeId)
            .Select(g => (EmployeeId: g.Key, StartDate: g.Min(x => x.Range.Start), EndDate: g.Max(x => x.Range.End)))
            .ToList();

        await _consolidationRepo.RequestRangesAsync(ranges, cancellationToken);
        foreach (var range in ranges)
        {
            _tracker.MarkRequested(range.EmployeeId, range.StartDate, range.EndDate);
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
