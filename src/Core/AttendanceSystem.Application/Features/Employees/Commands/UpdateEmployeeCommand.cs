using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Abstractions;
using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Aggregates.BranchAggregate;
using AttendanceSystem.Domain.Aggregates.DepartmentAggregate;
using AttendanceSystem.Domain.Aggregates.PositionAggregate;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using AttendanceSystem.Domain.Enumerations;
using MediatR;
using Microsoft.Extensions.Logging;



namespace AttendanceSystem.Application.Features.Employees.Commands;

[RequiresPermission(Permissions.EmployeesEdit)]
public sealed record UpdateEmployeeCommand(
    string Id,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    DateTime HireDate,
    Gender Gender,
    EmployeeStatus Status,
    string BranchId,
    string DepartmentId,
    string PositionId,
    ShiftType? ShiftType,
    string? ScheduleId,
    int? RestDay,
    bool OvertimeAuthorized,
    OvertimeCalculationMethod OvertimeCalculationMethod,
    OvertimeCapType OvertimeCapType,
    double? OvertimeCapMinutes,
    bool CalculateOvertimeBeforeEntry,
    string? CardNumber = null,
    string? DevicePassword = null,
    string? Photo = null,
    DevicePrivilege DevicePrivilege = DevicePrivilege.User,
    List<int>? RestDays = null,
    DateTime? EffectiveFrom = null,
    DateTime? EffectiveTo = null) : IRequest<Result<EmployeeDto>>;

public sealed class UpdateEmployeeCommandHandler : IRequestHandler<UpdateEmployeeCommand, Result<EmployeeDto>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IShiftRepository _shiftRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceCalculationLockService _lockService;
    private readonly IAttendanceConsolidationRepository _consolidations;
    private readonly IConsolidationWakeup _wakeup;
    private readonly IShiftRosterRepository _rosterRepository;
    private readonly IRotationPatternRepository? _patternRepository;
    private readonly ILogger<UpdateEmployeeCommandHandler> _logger;
    private readonly IPermissionService? _permissions;

    public UpdateEmployeeCommandHandler(
        IEmployeeRepository employeeRepository,
        IBranchRepository branchRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository,
        IShiftRepository shiftRepository,
        IUnitOfWork unitOfWork,
        IAttendanceCalculationLockService lockService,
        IAttendanceConsolidationRepository consolidations,
        IConsolidationWakeup wakeup,
        IShiftRosterRepository rosterRepository,
        ILogger<UpdateEmployeeCommandHandler> logger,
        IRotationPatternRepository? patternRepository = null,
        IPermissionService? permissions = null)
    {
        _permissions = permissions;
        _patternRepository = patternRepository;
        _rosterRepository = rosterRepository;
        _lockService = lockService;
        _consolidations = consolidations;
        _wakeup = wakeup;
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _shiftRepository = shiftRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<EmployeeDto>> Handle(UpdateEmployeeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var employeeId = EmployeeId.From(request.Id);
            var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken);

            if (employee is null)
            {
                return Result<EmployeeDto>.Failure($"No existe el empleado con ID {request.Id}");
            }

            if (EmployeeFieldPermissions.ForUpdate(_permissions, employee, request) is { } denied)
            {
                return Result<EmployeeDto>.Failure(denied);
            }

            // Validar que existan las entidades relacionadas
            var branchId = BranchId.From(request.BranchId);
            var branch = await _branchRepository.GetByIdAsync(branchId, cancellationToken);
            if (branch is null)
            {
                return Result<EmployeeDto>.Failure($"No existe la sucursal con ID {request.BranchId}");
            }

            if (branch.IsExternal)
            {
                return Result<EmployeeDto>.Failure($"La sucursal '{branch.Name}' es externa. Los empleados de sucursales externas deben gestionarse en el catálogo de Empleados Externos.");
            }

            var departmentId = DepartmentId.From(request.DepartmentId);
            var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken);
            if (department is null)
            {
                return Result<EmployeeDto>.Failure($"No existe el departamento con ID {request.DepartmentId}");
            }

            var positionId = PositionId.From(request.PositionId);
            var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
            if (position is null)
            {
                return Result<EmployeeDto>.Failure($"No existe el puesto con ID {request.PositionId}");
            }

            if (!department.Positions.Any(p => p.Id == positionId))
            {
                return Result<EmployeeDto>.Failure($"El puesto '{position.Name}' no pertenece al departamento '{department.Name}'");
            }

            // Dar de baja no exige horario: después de la baja no hay jornadas.
            if (request.Status != EmployeeStatus.Baja &&
                EmployeeScheduleRules.Validate(request.ShiftType, request.ScheduleId) is { } scheduleError)
            {
                return Result<EmployeeDto>.Failure(scheduleError);
            }

            ShiftId? scheduleId = null;
            Shift? schedule = null;
            if (!string.IsNullOrWhiteSpace(request.ScheduleId))
            {
                scheduleId = ShiftId.From(request.ScheduleId);
                schedule = await _shiftRepository.GetByIdAsync(scheduleId, cancellationToken);
                if (schedule is null)
                {
                    return Result<EmployeeDto>.Failure($"No existe el horario con ID {request.ScheduleId}");
                }

                if (request.ShiftType.HasValue && request.ShiftType.Value != ShiftType.Rotativo && schedule.ShiftType != request.ShiftType.Value)
                {
                    return Result<EmployeeDto>.Failure($"El horario '{schedule.Name}' no corresponde al turno seleccionado ({request.ShiftType.Value})");
                }
            }

            var restDays = request.RestDays?.Select(d => (WeekDay)d).ToList();

            DateTime? recalculateFrom = null;
            var saved = await ExclusiveAttendanceWrite.RunAsync(_unitOfWork, _lockService, new[] { employee.Id }, async () =>
            {
                recalculateFrom = employee.Update(
                    request.FirstName,
                    request.LastName,
                    request.Email,
                    request.PhoneNumber,
                    request.HireDate,
                    request.Gender,
                    request.Status,
                    branchId,
                    departmentId,
                    positionId,
                    request.ShiftType,
                    scheduleId,
                    (WeekDay?)request.RestDay,
                    request.OvertimeAuthorized,
                    request.OvertimeCalculationMethod,
                    request.OvertimeCapType,
                    request.OvertimeCapMinutes,
                    request.CalculateOvertimeBeforeEntry,
                    request.CardNumber,
                    request.DevicePassword,
                    request.Photo,
                    request.DevicePrivilege,
                    restDays,
                    request.EffectiveFrom,
                    request.EffectiveTo);

                _employeeRepository.Update(employee);

                // El roster sólo rige a los rotativos: al dejar de serlo, se borra desde la fecha de vigencia.
                var effectiveFrom = (request.EffectiveFrom ?? DateTime.Today).Date;
                if (employee.AssignmentOn(effectiveFrom).ShiftType != ShiftType.Rotativo)
                {
                    var obsolete = await _rosterRepository.GetByEmployeeAsync(employee.Id, effectiveFrom, DateTime.MaxValue.Date, cancellationToken);
                    if (obsolete.Count > 0) _rosterRepository.RemoveRange(obsolete);
                    // Su patrón tampoco se sigue extendiendo.
                    if (_patternRepository != null &&
                        await _patternRepository.GetByEmployeeAsync(employee.Id, cancellationToken) is { } pattern)
                    {
                        pattern.EndBefore(effectiveFrom);
                    }
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // F06-08: un cambio con vigencia pasada recalcula de forma durable las jornadas afectadas.
                if (recalculateFrom is { } from && from <= DateTime.Today)
                {
                    await _consolidations.RequestAsync(employee.Id, from, DateTime.Today, cancellationToken);
                }
                return Result.Success();
            }, cancellationToken);

            if (saved.IsFailure) return Result<EmployeeDto>.Failure(saved.Error);
            if (recalculateFrom.HasValue) _wakeup.Signal();

            var effectiveRestDays = employee.GetEffectiveRestDays();

            var dto = new EmployeeDto
            {
                Id = employee.Id.Value,
                FirstName = employee.FirstName,
                LastName = employee.LastName,
                FullName = employee.GetFullName(),
                Email = employee.Email ?? string.Empty,
                PhoneNumber = employee.PhoneNumber,
                HireDate = employee.HireDate,
                Status = employee.Status,
                BranchId = employee.BranchId.Value,
                BranchName = branch.Name,
                DepartmentId = employee.DepartmentId.Value,
                DepartmentName = department.Name,
                PositionId = employee.PositionId.Value,
                PositionName = position.Name,
                ShiftType = employee.ShiftType,
                ScheduleId = employee.ScheduleId?.Value,
                ScheduleName = schedule?.Name,
                RestDay = (int?)employee.RestDay,
                RestDays = effectiveRestDays.Select(d => (int)d).ToList(),
                RestDayName = effectiveRestDays.Any() ? string.Join(", ", effectiveRestDays.Select(d => GetDayName((int)d))) : null,
                OvertimeAuthorized = employee.OvertimeAuthorized,
                Gender = employee.Gender,
                OvertimeCalculationMethod = employee.OvertimeCalculationMethod,
                OvertimeCapType = employee.OvertimeCapType,
                OvertimeCapMinutes = employee.OvertimeCapMinutes,
                CalculateOvertimeBeforeEntry = employee.CalculateOvertimeBeforeEntry,
                CardNumber = employee.CardNumber,
                DevicePassword = employee.DevicePassword,
                Photo = employee.Photo,
                DevicePrivilege = employee.DevicePrivilege,

                StatusPeriods = employee.StatusPeriods.OrderByDescending(p => p.StartDate).Select(p => new StatusPeriodDto(p.Status, p.StartDate, p.EndDate)).ToList()
            };

            _logger.LogInformation("Empleado actualizado: {EmployeeId} - {FullName}", employee.Id.Value, employee.GetFullName());

            return Result<EmployeeDto>.Success(dto);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Error de dominio al actualizar empleado");
            return Result<EmployeeDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al actualizar empleado");
            return Result<EmployeeDto>.Failure("Error al actualizar el empleado");
        }
    }

    private static string GetDayName(int day) => day switch
    {
        0 => "Domingo",
        1 => "Lunes",
        2 => "Martes",
        3 => "Miércoles",
        4 => "Jueves",
        5 => "Viernes",
        6 => "Sábado",
        _ => "N/A"
    };
}
