using AttendanceSystem.Domain.Services;
using MediatR;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.Logging;

using AttendanceSystem.Domain.Aggregates.DailyAttendanceAggregate;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;

public record ProcessMixAttendanceCommand(
    Employee Employee,
    DateTime Date,
    Shift Shift,
    List<AttendanceRecord> Records,
    bool IsRestDay,
    AttendanceAdjustment? Adjustment = null,
    string? TimeZoneId = null,
    PunchAssignmentDraft? Draft = null,
    DateTime? NextJornadaEntryStart = null,
    LunchPolicy? Lunch = null) : IRequest;

public class ProcessMixAttendanceCommandHandler : IRequestHandler<ProcessMixAttendanceCommand>
{
    private readonly ISender _sender;
    private readonly ILogger<ProcessMixAttendanceCommandHandler> _logger;

    public ProcessMixAttendanceCommandHandler(
        ISender sender,
        ILogger<ProcessMixAttendanceCommandHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Handle(ProcessMixAttendanceCommand request, CancellationToken cancellationToken)
    {
        var dayConfig = request.Shift.Days.FirstOrDefault(d => d.DayOfWeek == request.Date.DayOfWeek);

        if (dayConfig == null)
        {
            // Sin configuración para este día: es descanso salvo decisión manual explícita (C-05 / A02),
            // y el ajuste se conserva (asignaciones, salidas temporales, extras).
            var isRestDay = request.Adjustment?.IsRestDayOverride ?? true;
            _logger.LogDebug(
                "Empleado {EmpId} - {Date}: Turno mixto sin configuración para el día de la semana {DayOfWeek}. Delegando a ProcessNoShiftAttendanceCommand (descanso={IsRestDay}).",
                request.Employee.Id.Value, request.Date.ToString("dd/MM/yyyy"), request.Date.DayOfWeek, isRestDay);

            await _sender.Send(new ProcessNoShiftAttendanceCommand(
                request.Employee,
                request.Date,
                request.Records,
                IsRestDay: isRestDay,
                Adjustment: request.Adjustment,
                TimeZoneId: request.TimeZoneId,
                Draft: request.Draft), cancellationToken);

            return;
        }

        var dayShiftType = dayConfig.ShiftType;
        _logger.LogInformation(
            "Empleado {EmpId} - {Date}: Turno mixto. Día de la semana {DayOfWeek} configurado como tipo {DayShiftType}.",
            request.Employee.Id.Value, request.Date.ToString("dd/MM/yyyy"), request.Date.DayOfWeek, dayShiftType);

        // F06-04: los procesadores reciben la configuración efectiva del día (tipo, inicio y duración), no la
        // del turno base; antes un vespertino de las 14:00 se evaluaba contra el inicio de las 08:00.
        var effective = request.Shift.EffectiveFor(dayConfig);
        string? issue = null;

        switch (dayShiftType)
        {
            case ShiftType.Nocturno:
                await _sender.Send(new ProcessNightlyAttendanceCommand(
                    request.Employee, request.Date, effective, request.Records, request.IsRestDay,
                    dayConfig.StartTime, dayConfig.EndTime,
                    Adjustment: request.Adjustment, TimeZoneId: request.TimeZoneId, Draft: request.Draft), cancellationToken);
                break;

            case ShiftType.Continuo:
                await _sender.Send(new ProcessContinuousAttendanceCommand(
                    request.Employee, request.Date, effective, request.Records, request.IsRestDay,
                    Adjustment: request.Adjustment, TimeZoneId: request.TimeZoneId, Draft: request.Draft,
                    NextJornadaEntryStart: request.NextJornadaEntryStart, Lunch: request.Lunch), cancellationToken);
                break;

            case ShiftType.Flexible:
                await _sender.Send(new ProcessFlexibleAttendanceCommand(
                    request.Employee, request.Date, effective, request.Records, request.IsRestDay,
                    Adjustment: request.Adjustment, TimeZoneId: request.TimeZoneId, Draft: request.Draft,
                    NextJornadaEntryStart: request.NextJornadaEntryStart, Lunch: request.Lunch), cancellationToken);
                break;

            case ShiftType.Partido:
                // La configuración diaria no define el segundo bloque: el procesador partido lo calcula como un solo
                // bloque y registra la incidencia de configuración incompleta.
                await _sender.Send(new ProcessSplitAttendanceCommand(
                    request.Employee, request.Date, effective, request.Records, request.IsRestDay,
                    Adjustment: request.Adjustment, TimeZoneId: request.TimeZoneId, Draft: request.Draft), cancellationToken);
                break;

            case ShiftType.Matutino:
            case ShiftType.Vespertino:
                await _sender.Send(new ProcessRegularAttendanceCommand(
                    request.Employee, request.Date, effective, request.Records, request.IsRestDay,
                    Adjustment: request.Adjustment, TimeZoneId: request.TimeZoneId, Draft: request.Draft, Lunch: request.Lunch), cancellationToken);
                break;

            default:
                issue = $"Tipo {dayShiftType} no soportado en la configuración diaria de turno mixto; calculado como regular.";
                _logger.LogWarning("Tipo de turno no soportado en turno mixto diario: {Type}. Usando regular.", dayShiftType);
                await _sender.Send(new ProcessRegularAttendanceCommand(
                    request.Employee, request.Date, effective, request.Records, request.IsRestDay,
                    Adjustment: request.Adjustment, TimeZoneId: request.TimeZoneId, Draft: request.Draft, Lunch: request.Lunch), cancellationToken);
                break;
        }

        if (issue != null)
        {
            request.Draft?.DailyAttendance?.AddCalculationIssue(issue);
        }
    }
}
