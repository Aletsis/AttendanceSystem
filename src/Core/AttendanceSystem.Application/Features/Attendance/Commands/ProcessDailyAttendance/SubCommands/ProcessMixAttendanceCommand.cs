using MediatR;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Enumerations;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ProcessDailyAttendance.SubCommands;

public record ProcessMixAttendanceCommand(
    Employee Employee,
    DateTime Date,
    Shift Shift,
    List<AttendanceRecord> Records,
    bool IsRestDay) : IRequest;

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
            // Sin configuración para este día: se asume día de descanso / libre
            _logger.LogDebug(
                "Empleado {EmpId} - {Date}: Turno mixto sin configuración para el día de la semana {DayOfWeek}. Delegando a ProcessNoShiftAttendanceCommand como día de descanso.",
                request.Employee.Id.Value, request.Date.ToString("dd/MM/yyyy"), request.Date.DayOfWeek);

            await _sender.Send(new ProcessNoShiftAttendanceCommand(
                request.Employee,
                request.Date,
                request.Records,
                IsRestDay: true), cancellationToken);
            
            return;
        }

        var dayShiftType = dayConfig.ShiftType;
        _logger.LogInformation(
            "Empleado {EmpId} - {Date}: Turno mixto. Día de la semana {DayOfWeek} configurado como tipo {DayShiftType}.",
            request.Employee.Id.Value, request.Date.ToString("dd/MM/yyyy"), request.Date.DayOfWeek, dayShiftType);

        switch (dayShiftType)
        {
            case ShiftType.Matutino:
            case ShiftType.Vespertino:
                await _sender.Send(new ProcessRegularAttendanceCommand(
                    request.Employee,
                    request.Date,
                    request.Shift,
                    request.Records,
                    request.IsRestDay), cancellationToken);
                break;

            case ShiftType.Nocturno:
                await _sender.Send(new ProcessNightlyAttendanceCommand(
                    request.Employee,
                    request.Date,
                    request.Shift,
                    request.Records,
                    request.IsRestDay,
                    dayConfig.StartTime,
                    dayConfig.EndTime), cancellationToken);
                break;

            case ShiftType.Continuo:
                await _sender.Send(new ProcessContinuousAttendanceCommand(
                    request.Employee,
                    request.Date,
                    request.Shift,
                    request.Records,
                    request.IsRestDay), cancellationToken);
                break;

            default:
                // Fallback por seguridad
                _logger.LogWarning("Tipo de turno desconocido o no soportado en turno mixto diario: {Type}. Usando regular.", dayShiftType);
                await _sender.Send(new ProcessRegularAttendanceCommand(
                    request.Employee,
                    request.Date,
                    request.Shift,
                    request.Records,
                    request.IsRestDay), cancellationToken);
                break;
        }
    }
}
