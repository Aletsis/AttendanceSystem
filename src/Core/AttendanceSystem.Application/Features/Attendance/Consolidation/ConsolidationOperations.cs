using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Repositories;

namespace AttendanceSystem.Application.Features.Attendance.Consolidation;

public sealed record ConsolidationStatusDto(
    ConsolidationHealth Requests,
    OutboxHealth Outbox,
    TimeSpan? OldestPendingRequestAge,
    TimeSpan? OldestPendingEventAge);

/// <summary>
/// Estado operativo de la consolidación (F04-08): pendientes, en proceso, fallidas con su causa e
/// intentos, mensajes muertos y antigüedad del trabajo pendiente más viejo.
/// </summary>
public sealed record GetConsolidationStatusQuery(int RecentFailures = 20) : IRequest<ConsolidationStatusDto>;

public sealed class GetConsolidationStatusQueryHandler : IRequestHandler<GetConsolidationStatusQuery, ConsolidationStatusDto>
{
    private readonly IAttendanceConsolidationRepository _repository;
    private readonly IOutboxMonitor _outbox;

    public GetConsolidationStatusQueryHandler(IAttendanceConsolidationRepository repository, IOutboxMonitor outbox)
    {
        _repository = repository;
        _outbox = outbox;
    }

    public async Task<ConsolidationStatusDto> Handle(GetConsolidationStatusQuery request, CancellationToken cancellationToken)
    {
        var requests = await _repository.GetHealthAsync(request.RecentFailures, cancellationToken);
        var outbox = await _outbox.GetHealthAsync(request.RecentFailures, cancellationToken);
        var now = DateTime.UtcNow;

        return new ConsolidationStatusDto(
            requests,
            outbox,
            requests.OldestPendingRequestedAt is { } r ? now - r : null,
            outbox.OldestPendingOccurredOnUtc is { } o ? now - o : null);
    }
}

/// <summary>Reintento operativo de solicitudes fallidas (todas o una) tras corregir su causa.</summary>
[RequiresPermission(Permissions.SettingsManage)]
public sealed record RetryFailedConsolidationsCommand(Guid? RequestId = null) : IRequest<Result<int>>;

public sealed class RetryFailedConsolidationsCommandHandler : IRequestHandler<RetryFailedConsolidationsCommand, Result<int>>
{
    private readonly IAttendanceConsolidationRepository _repository;
    private readonly IConsolidationWakeup _wakeup;

    public RetryFailedConsolidationsCommandHandler(IAttendanceConsolidationRepository repository, IConsolidationWakeup wakeup)
    {
        _repository = repository;
        _wakeup = wakeup;
    }

    public async Task<Result<int>> Handle(RetryFailedConsolidationsCommand request, CancellationToken cancellationToken)
    {
        var count = await _repository.RetryFailedAsync(request.RequestId, cancellationToken);
        if (count > 0) _wakeup.Signal();
        return Result<int>.Success(count);
    }
}

/// <summary>Reintento operativo de eventos del outbox que agotaron sus intentos.</summary>
[RequiresPermission(Permissions.SettingsManage)]
public sealed record RetryDeadLetteredEventsCommand(Guid? MessageId = null) : IRequest<Result<int>>;

public sealed class RetryDeadLetteredEventsCommandHandler : IRequestHandler<RetryDeadLetteredEventsCommand, Result<int>>
{
    private readonly IOutboxMonitor _outbox;

    public RetryDeadLetteredEventsCommandHandler(IOutboxMonitor outbox)
    {
        _outbox = outbox;
    }

    public async Task<Result<int>> Handle(RetryDeadLetteredEventsCommand request, CancellationToken cancellationToken) =>
        Result<int>.Success(await _outbox.ReviveDeadLettersAsync(request.MessageId, cancellationToken));
}
