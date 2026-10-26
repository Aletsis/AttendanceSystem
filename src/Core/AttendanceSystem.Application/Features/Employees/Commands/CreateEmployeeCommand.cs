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

[RequiresPermission(Permissions.EmployeesCreate)]
public sealed record CreateEmployeeCommand(
    string Id,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    DateTime HireDate,
    Gender Gender,
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
    List<int>? RestDays = null) : IRequest<Result<EmployeeDto>>;

public sealed class CreateEmployeeCommandHandler : IRequestHandler<CreateEmployeeCommand, Result<EmployeeDto>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IShiftRepository _shiftRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateEmployeeCommandHandler> _logger;
    private readonly IPermissionService? _permissions;
    private readonly IAttendanceRepository? _attendance;
    private readonly IAttendanceConsolidationRepository? _consolidations;
    private readonly IConsolidationWakeup? _wakeup;

    public CreateEmployeeCommandHandler(
        IEmployeeRepository employeeRepository,
        IBranchRepository branchRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository,
        IShiftRepository shiftRepository,
        IUnitOfWork unitOfWork,
        ILogger<CreateEmployeeCommandHandler> logger,
        IPermissionService? permissions = null,
        IAttendanceRepository? attendance = null,
        IAttendanceConsolidationRepository? consolidations = null,
        IConsolidationWakeup? wakeup = null)
    {
        _permissions = permissions;
        _attendance = attendance;
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

    public async Task<Result<EmployeeDto>> Handle(CreateEmployeeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (EmployeeFieldPermissions.ForCreate(_permissions, request.OvertimeAuthorized, request.DevicePrivilege) is { } denied)
            {
                return Result<EmployeeDto>.Failure(denied);
            }

            var employeeId = EmployeeId.From(request.Id);

            // Verificar si el empleado ya existe
            if (await _employeeRepository.ExistsAsync(employeeId, cancellationToken))
            {
                return Result<EmployeeDto>.Failure($"Ya existe un empleado con el ID {request.Id}");
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

            if (EmployeeScheduleRules.Validate(request.ShiftType, request.ScheduleId) is { } scheduleError)
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

            var employee = Employee.Create(
                employeeId,
                request.FirstName,
                request.LastName,
                request.Email,
                request.PhoneNumber,
                request.HireDate,
                request.Gender,
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
                restDays);

            _employeeRepository.Add(employee);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await RecalculateExistingPunchesAsync(employee, cancellationToken);

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

            _logger.LogInformation("Empleado creado: {EmployeeId} - {FullName}", employee.Id.Value, employee.GetFullName());

            return Result<EmployeeDto>.Success(dto);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Error de dominio al crear empleado");
            return Result<EmployeeDto>.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al crear empleado");
            return Result<EmployeeDto>.Failure("Error al crear el empleado");
        }
    }

    /// <summary>
    /// Caso 14: si el reloj ya tenía marcajes de este ID (aviso de marcajes sin empleado), se calculan desde el ingreso.
    /// </summary>
    private async Task RecalculateExistingPunchesAsync(Employee employee, CancellationToken cancellationToken)
    {
        if (_attendance == null || _consolidations == null) return;
        var today = DateTime.Today;
        if (employee.HireDate.Date > today) return;

        var punches = await _attendance.GetByDateRangeAsync(DateOnly.FromDateTime(employee.HireDate), DateOnly.FromDateTime(today), employee.Id, cancellationToken);
        var first = punches.Where(r => !r.IsVoided).Select(r => r.GetWorkDate()).DefaultIfEmpty().Min();
        if (first == default) return;

        await _consolidations.RequestAsync(employee.Id, first.ToDateTime(TimeOnly.MinValue), today, cancellationToken);
        _wakeup?.Signal();
        _logger.LogInformation("Empleado {EmployeeId} dado de alta con marcajes previos desde {From:yyyy-MM-dd}: se recalcularán.", employee.Id.Value, first);
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
