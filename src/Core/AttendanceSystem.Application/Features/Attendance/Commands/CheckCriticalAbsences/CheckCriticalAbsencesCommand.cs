using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.AttendanceAggregate;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Aggregates.SystemAlertAggregate;
using AttendanceSystem.Domain.Enumerations;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;

namespace AttendanceSystem.Application.Features.Attendance.Commands.CheckCriticalAbsences;

public sealed record CheckCriticalAbsencesCommand() : IRequest<Result<int>>;

public sealed class CheckCriticalAbsencesCommandHandler : IRequestHandler<CheckCriticalAbsencesCommand, Result<int>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly IPositionRepository _positionRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IShiftRepository _shiftRepository;
    private readonly ISystemAlertRepository _alertRepository;

    public CheckCriticalAbsencesCommandHandler(
        IUnitOfWork unitOfWork,
        IEmailService emailService,
        IPositionRepository positionRepository,
        IEmployeeRepository employeeRepository,
        IAttendanceRepository attendanceRepository,
        IShiftRepository shiftRepository,
        ISystemAlertRepository alertRepository)
    {
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _positionRepository = positionRepository;
        _employeeRepository = employeeRepository;
        _attendanceRepository = attendanceRepository;
        _shiftRepository = shiftRepository;
        _alertRepository = alertRepository;
    }

    public async Task<Result<int>> Handle(CheckCriticalAbsencesCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, timeZone);
            var today = localNow.Date;
            var currentTime = localNow.TimeOfDay;

            // 1. Get Critical Position IDs
            var positions = await _positionRepository.GetAllAsync(cancellationToken);
            var criticalPositionIds = positions.Where(p => p.IsCritical).Select(p => p.Id).ToList();

            if (!criticalPositionIds.Any()) return Result<int>.Success(0);

            // 2. Get Active Employees
            var allEmployees = await _employeeRepository.GetAllAsync(cancellationToken);
            var employees = allEmployees
                .Where(e => criticalPositionIds.Contains(e.PositionId) && e.Status == EmployeeStatus.Alta)
                .ToList();

            int alertsSent = 0;

            foreach (var employee in employees)
            {
                if (employee.ScheduleId == null) continue;

                var shift = await _shiftRepository.GetByIdAsync(employee.ScheduleId, cancellationToken);
                if (shift == null) continue;

                bool isWorkingDay = true;
                TimeSpan startTime = shift.StartTime;

                if (shift.ShiftType == ShiftType.Mixto)
                {
                    var dayConfig = shift.Days.FirstOrDefault(d => d.DayOfWeek == localNow.DayOfWeek);
                    if (dayConfig == null) isWorkingDay = false;
                    else startTime = dayConfig.StartTime;
                }

                if (!isWorkingDay) continue;

                var alertThreshold = startTime.Add(TimeSpan.FromMinutes(15));
                if (currentTime < alertThreshold) continue;

                // 5. Check if already checked in today
                var hasCheckIn = await _attendanceRepository.HasCheckInForDateAsync(employee.Id, today, cancellationToken);
                if (hasCheckIn) continue;

                // 6. Check if alert already sent today
                var alertKey = $"{employee.Id.Value}_{today:yyyyMMdd}";
                var alreadyAlerted = await _alertRepository.ExistsAsync(AlertType.CriticalPositionAbsence, alertKey, cancellationToken);

                if (alreadyAlerted) continue;

                // 7. SEND ALERT
                var positionName = positions.First(p => p.Id == employee.PositionId).Name;
                var message = $"ALERTA CRÍTICA: El empleado {employee.FirstName} {employee.LastName} no ha registrado su entrada. Puesto crítico: {positionName}. Hora de entrada esperada: {startTime:hh\\:mm}.";
                
                await _emailService.SendAlertAsync(
                    "Alerta de Puesto Crítico no Cubierto",
                    message,
                    AlertLevel.Absence,
                    cancellationToken);

                // 8. Log Alert
                var alert = SystemAlert.Create(AlertType.CriticalPositionAbsence, alertKey, message);
                await _alertRepository.AddAsync(alert, cancellationToken);
                
                alertsSent++;
            }

            if (alertsSent > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<int>.Success(alertsSent);
        }
        catch (Exception ex)
        {
            return Result<int>.Failure($"Error al procesar alertas críticas: {ex.Message}");
        }
    }
}
