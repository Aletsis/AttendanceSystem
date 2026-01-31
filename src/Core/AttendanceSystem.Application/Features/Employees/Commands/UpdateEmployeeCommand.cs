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
    double? OvertimeCapMinutes) : IRequest<Result<EmployeeDto>>;

public sealed class UpdateEmployeeCommandHandler : IRequestHandler<UpdateEmployeeCommand, Result<EmployeeDto>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IShiftRepository _shiftRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateEmployeeCommandHandler> _logger;

    public UpdateEmployeeCommandHandler(
        IEmployeeRepository employeeRepository,
        IBranchRepository branchRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository,
        IShiftRepository shiftRepository,
        IUnitOfWork unitOfWork,
        ILogger<UpdateEmployeeCommandHandler> logger)
    {
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

            // Validar que existan las entidades relacionadas
            var branchId = BranchId.From(request.BranchId);
            var branch = await _branchRepository.GetByIdAsync(branchId, cancellationToken);
            if (branch is null)
            {
                return Result<EmployeeDto>.Failure($"No existe la sucursal con ID {request.BranchId}");
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
            }

            employee.Update(
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
                request.OvertimeCapMinutes);

            _employeeRepository.Update(employee);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

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
                RestDayName = employee.RestDay.HasValue ? GetDayName((int)employee.RestDay.Value) : null,
                OvertimeAuthorized = employee.OvertimeAuthorized,
                Gender = employee.Gender,
                OvertimeCalculationMethod = employee.OvertimeCalculationMethod,
                OvertimeCapType = employee.OvertimeCapType,
                OvertimeCapMinutes = employee.OvertimeCapMinutes
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
