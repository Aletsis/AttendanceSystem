using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Devices.Quarantine;

public sealed record QuarantinedPunchDto(
    Guid Id, string EmployeeId, string EmployeeName, string DeviceId, string DeviceName, DateTime ReportedTime, string TimeZoneId,
    string Reason, DateTime ReceivedAtUtc, QuarantineStatus Status, DateTime? ReleasedTime, string? ResolvedBy, string? ResolutionNote,
    string? Rule = null, int? SuggestedOffsetMinutes = null);

public sealed record PunchTimeCorrectionDto(
    Guid Id, string DeviceName, DateOnly FromDate, DateOnly ToDate, string? TimeZoneId, int OffsetMinutes, string Reason,
    string AppliedBy, DateTime AppliedAtUtc, int CorrectedCount, int SkippedCount);

public sealed record ClockAlertDto(Guid Id, AlertType Type, string DeviceName, DateTime TimestampUtc, string Message);

public sealed record GetQuarantinedPunchesQuery(QuarantineStatus? Status = QuarantineStatus.Pending, int Take = 500) : IRequest<IReadOnlyList<QuarantinedPunchDto>>;
public sealed record GetPunchTimeCorrectionsQuery(int Take = 50) : IRequest<IReadOnlyList<PunchTimeCorrectionDto>>;
public sealed record GetClockAlertsQuery : IRequest<IReadOnlyList<ClockAlertDto>>;
[RequiresPermission(Permissions.ClockAlertsResolve)]
public sealed record ResolveClockAlertCommand(Guid Id) : IRequest<Result>;

public sealed class ClockTimeQueryHandler :
    IRequestHandler<GetQuarantinedPunchesQuery, IReadOnlyList<QuarantinedPunchDto>>,
    IRequestHandler<GetPunchTimeCorrectionsQuery, IReadOnlyList<PunchTimeCorrectionDto>>,
    IRequestHandler<GetClockAlertsQuery, IReadOnlyList<ClockAlertDto>>,
    IRequestHandler<ResolveClockAlertCommand, Result>
{
    private static readonly AlertType[] ClockAlertTypes = { AlertType.DeviceClockDrift, AlertType.PunchQuarantined };

    private readonly IQuarantinedPunchRepository _quarantine;
    private readonly IPunchTimeCorrectionRepository _corrections;
    private readonly ISystemAlertRepository _alerts;
    private readonly IDeviceRepository _devices;
    private readonly IEmployeeRepository _employees;
    private readonly IUnitOfWork _unitOfWork;

    public ClockTimeQueryHandler(IQuarantinedPunchRepository quarantine, IPunchTimeCorrectionRepository corrections, ISystemAlertRepository alerts,
        IDeviceRepository devices, IEmployeeRepository employees, IUnitOfWork unitOfWork)
    {
        _quarantine = quarantine;
        _corrections = corrections;
        _alerts = alerts;
        _devices = devices;
        _employees = employees;
        _unitOfWork = unitOfWork;
    }

    private async Task<Dictionary<string, string>> DeviceNamesAsync(CancellationToken cancellationToken) =>
        (await _devices.GetAllDevicesAsync(cancellationToken)).ToDictionary(d => d.Id.Value, d => d.Name);

    public async Task<IReadOnlyList<QuarantinedPunchDto>> Handle(GetQuarantinedPunchesQuery request, CancellationToken cancellationToken)
    {
        var punches = await _quarantine.GetAsync(request.Status, request.Take, cancellationToken);
        if (punches.Count == 0) return Array.Empty<QuarantinedPunchDto>();
        var devices = await DeviceNamesAsync(cancellationToken);
        var employees = (await _employees.GetAllAsync(cancellationToken)).ToDictionary(e => e.Id.Value, e => $"{e.FirstName} {e.LastName}");
        return punches.Select(p => new QuarantinedPunchDto(p.Id, p.EmployeeId, employees.GetValueOrDefault(p.EmployeeId, "(no registrado)"),
            p.DeviceId.Value, devices.GetValueOrDefault(p.DeviceId.Value, p.DeviceId.Value), DateTime.SpecifyKind(p.ReportedTime, DateTimeKind.Unspecified),
            p.TimeZoneId, p.Reason, p.ReceivedAtUtc, p.Status, p.ReleasedTime is { } r ? DateTime.SpecifyKind(r, DateTimeKind.Unspecified) : null,
            p.ResolvedBy, p.ResolutionNote, p.Rule, p.SuggestedOffsetMinutes)).ToList();
    }

    public async Task<IReadOnlyList<PunchTimeCorrectionDto>> Handle(GetPunchTimeCorrectionsQuery request, CancellationToken cancellationToken)
    {
        var corrections = await _corrections.GetRecentAsync(request.Take, cancellationToken);
        var devices = await DeviceNamesAsync(cancellationToken);
        return corrections.Select(c => new PunchTimeCorrectionDto(c.Id, devices.GetValueOrDefault(c.DeviceId.Value, c.DeviceId.Value), c.FromDate, c.ToDate,
            c.TimeZoneId, c.OffsetMinutes, c.Reason, c.AppliedBy, c.AppliedAtUtc, c.Entries.Count, c.SkippedCount)).ToList();
    }

    public async Task<IReadOnlyList<ClockAlertDto>> Handle(GetClockAlertsQuery request, CancellationToken cancellationToken)
    {
        var alerts = (await _alerts.GetActiveAlertsAsync(cancellationToken)).Where(a => ClockAlertTypes.Contains(a.Type)).ToList();
        if (alerts.Count == 0) return Array.Empty<ClockAlertDto>();
        var devices = await DeviceNamesAsync(cancellationToken);
        return alerts.OrderByDescending(a => a.Timestamp)
            .Select(a => new ClockAlertDto(a.Id.Value, a.Type, a.ReferenceId != null ? devices.GetValueOrDefault(a.ReferenceId, a.ReferenceId) : "-", a.Timestamp, a.Message))
            .ToList();
    }

    public async Task<Result> Handle(ResolveClockAlertCommand request, CancellationToken cancellationToken)
    {
        var alert = (await _alerts.GetActiveAlertsAsync(cancellationToken)).FirstOrDefault(a => a.Id.Value == request.Id);
        if (alert == null) return Result.Failure("La alerta no existe o ya fue atendida.");
        alert.Resolve();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
