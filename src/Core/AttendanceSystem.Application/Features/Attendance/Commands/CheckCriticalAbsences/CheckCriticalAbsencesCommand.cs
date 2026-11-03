using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.Services;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.CheckCriticalAbsences;

[RequiresPermission(Permissions.SettingsManage)]
public sealed record CheckCriticalAbsencesCommand() : IRequest<Result<int>>;

public sealed class CheckCriticalAbsencesCommandHandler : IRequestHandler<CheckCriticalAbsencesCommand, Result<int>>
{
    /// <summary>Minutos después de la hora de entrada a partir de los cuales se avisa.</summary>
    public static readonly TimeSpan AlertAfter = TimeSpan.FromMinutes(15);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly IPositionRepository _positionRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IShiftRepository _shiftRepository;
    private readonly ISystemAlertRepository _alertRepository;
    private readonly IAuthoritativeTimeZoneResolver _timeZoneResolver;
    private readonly ITimeNormalizer _timeNormalizer;
    private readonly IShiftRosterRepository? _rosterRepository;
    private readonly IAttendanceAdjustmentRepository? _adjustmentRepository;
    private readonly ILogger<CheckCriticalAbsencesCommandHandler>? _logger;
    private readonly TimeProvider _timeProvider;

    public CheckCriticalAbsencesCommandHandler(
        IUnitOfWork unitOfWork,
        IEmailService emailService,
        IPositionRepository positionRepository,
        IEmployeeRepository employeeRepository,
        IAttendanceRepository attendanceRepository,
        IShiftRepository shiftRepository,
        ISystemAlertRepository alertRepository,
        IAuthoritativeTimeZoneResolver? timeZoneResolver = null,
        ITimeNormalizer? timeNormalizer = null,
        IShiftRosterRepository? rosterRepository = null,
        IAttendanceAdjustmentRepository? adjustmentRepository = null,
        ILogger<CheckCriticalAbsencesCommandHandler>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _rosterRepository = rosterRepository;
        _adjustmentRepository = adjustmentRepository;
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _positionRepository = positionRepository;
        _employeeRepository = employeeRepository;
        _attendanceRepository = attendanceRepository;
        _shiftRepository = shiftRepository;
        _alertRepository = alertRepository;
        _timeZoneResolver = timeZoneResolver ?? DefaultAuthoritativeTimeZoneResolver.Instance;
        _timeNormalizer = timeNormalizer ?? DefaultTimeNormalizer.Instance;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Cada alerta se envía y se registra de inmediato, una por una: antes se registraban todas al final y, si fallaba
    /// el envío de una, las ya enviadas no quedaban registradas y se reenviaban en cada pasada. Si un envío falla, esa
    /// alerta no se registra (se reintenta en la siguiente pasada) y las demás siguen. La tarea corre una sola vez a la vez,
    /// así que dos pasadas no envían la misma alerta.
    /// </summary>
    public async Task<Result<int>> Handle(CheckCriticalAbsencesCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var systemTimeZone = await _timeZoneResolver.ResolveForBranchAsync(null, cancellationToken);
            var localNow = _timeNormalizer.ToCivilTime(_timeProvider.GetUtcNow().UtcDateTime, systemTimeZone);
            var today = localNow.Date;

            // 1. Get Critical Position IDs
            var positions = await _positionRepository.GetAllAsync(cancellationToken);
            var criticalPositionIds = positions.Where(p => p.IsCritical).Select(p => p.Id).ToList();

            if (!criticalPositionIds.Any()) return Result<int>.Success(0);

            var allEmployees = await _employeeRepository.GetAllAsync(cancellationToken);
            var shifts = (await _shiftRepository.GetAllAsync(cancellationToken)).ToDictionary(s => s.Id);

            int alertsSent = 0;
            var failures = new List<string>();

            // La jornada de ayer cuenta cuando su entrada fue tan tarde (después de las 23:45) que el aviso cae hoy.
            foreach (var workDate in new[] { today.AddDays(-1), today })
            {
                // F06-08: período de empleo vigente ese día (no sólo el estatus actual).
                foreach (var employee in allEmployees.Where(e => criticalPositionIds.Contains(e.PositionId) && e.IsActiveOn(workDate)))
                {
                    // Misma resolución que el cálculo: ajuste del día, roster, descanso, horario vigente y día del mixto.
                    var roster = _rosterRepository == null
                        ? null
                        : (await _rosterRepository.GetByDateRangeAsync(workDate, workDate, null, employee.Id, cancellationToken)).FirstOrDefault();
                    var adjustment = _adjustmentRepository == null
                        ? null
                        : await _adjustmentRepository.GetByEmployeeAndDateAsync(employee.Id, workDate, cancellationToken);
                    var resolution = AttendanceShiftResolutionService.Resolve(employee, workDate, roster, adjustment, shifts);
                    if (resolution.Shift == null || resolution.IsRestDay) continue;

                    TimeSpan startTime = resolution.Shift.StartTime;
                    var alertAt = workDate.Add(startTime).Add(AlertAfter);
                    if (localNow < alertAt) continue;
                    if (workDate < today && alertAt.Date < today) continue; // ya se revisó ayer

                    // 5. Check if already checked in (una entrada pasada la medianoche cuenta para la jornada de ayer)
                    var hasCheckIn = await _attendanceRepository.HasCheckInForDateAsync(employee.Id, workDate, cancellationToken) ||
                                     (workDate < today && await _attendanceRepository.HasCheckInForDateAsync(employee.Id, today, cancellationToken));
                    if (hasCheckIn) continue;

                    // 6. Check if alert already sent for that day
                    var alertKey = $"{employee.Id.Value}_{workDate:yyyyMMdd}";
                    var alreadyAlerted = await _alertRepository.ExistsAsync(AlertType.CriticalPositionAbsence, alertKey, cancellationToken);

                    if (alreadyAlerted) continue;

                    // 7. SEND ALERT
                    var positionName = positions.First(p => p.Id == employee.PositionId).Name;
                    var message = $"ALERTA CRÍTICA: El empleado {employee.FirstName} {employee.LastName} no ha registrado su entrada. Puesto crítico: {positionName}. Hora de entrada esperada: {startTime:hh\\:mm}" +
                                  (workDate < today ? $" del {workDate:dd/MM/yyyy}." : ".");

                    try
                    {
                        await _emailService.SendAlertAsync(
                            "Alerta de Puesto Crítico no Cubierto",
                            System.Net.WebUtility.HtmlEncode(message),
                            AlertLevel.Absence,
                            cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger?.LogWarning(ex, "No se pudo enviar la alerta de puesto crítico de {EmployeeId}; se reintentará en la siguiente pasada.", employee.Id.Value);
                        failures.Add($"{employee.Id.Value}: {ex.Message}");
                        continue;
                    }

                    // 8. Log Alert (en cuanto se envió)
                    await _alertRepository.AddAsync(SystemAlert.Create(AlertType.CriticalPositionAbsence, alertKey, message), cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    alertsSent++;
                }
            }

            return failures.Count == 0
                ? Result<int>.Success(alertsSent)
                : Result<int>.Failure($"Se enviaron {alertsSent} alertas; fallaron {failures.Count}: {string.Join("; ", failures)}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Failure($"Error al procesar alertas críticas: {ex.Message}");
        }
    }
}
