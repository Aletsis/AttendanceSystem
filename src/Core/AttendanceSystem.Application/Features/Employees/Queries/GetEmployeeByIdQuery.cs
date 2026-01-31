using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Domain.Aggregates.ShiftAggregate;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Employees.Queries;

public sealed record GetEmployeeByIdQuery(string Id) : IRequest<Result<EmployeeDto>>;

public sealed class GetEmployeeByIdQueryHandler : IRequestHandler<GetEmployeeByIdQuery, Result<EmployeeDto>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IShiftRepository _shiftRepository;
    private readonly ILogger<GetEmployeeByIdQueryHandler> _logger;

    public GetEmployeeByIdQueryHandler(
        IEmployeeRepository employeeRepository,
        IBranchRepository branchRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository,
        IShiftRepository shiftRepository,
        ILogger<GetEmployeeByIdQueryHandler> logger)
    {
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _shiftRepository = shiftRepository;
        _logger = logger;
    }

    public async Task<Result<EmployeeDto>> Handle(GetEmployeeByIdQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var employeeId = EmployeeId.From(request.Id);
            var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken);

            if (employee is null)
            {
                return Result<EmployeeDto>.Failure($"No existe el empleado con ID {request.Id}");
            }

            var branch = await _branchRepository.GetByIdAsync(employee.BranchId, cancellationToken);
            var department = await _departmentRepository.GetByIdAsync(employee.DepartmentId, cancellationToken);
            var position = await _positionRepository.GetByIdAsync(employee.PositionId, cancellationToken);
            

            
            Shift? schedule = null;
            if (employee.ScheduleId != null)
            {
                schedule = await _shiftRepository.GetByIdAsync(employee.ScheduleId, cancellationToken);
            }

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
                BranchName = branch?.Name ?? "N/A",
                DepartmentId = employee.DepartmentId.Value,
                DepartmentName = department?.Name ?? "N/A",
                PositionId = employee.PositionId.Value,
                PositionName = position?.Name ?? "N/A",
                ShiftType = employee.ShiftType,
                ScheduleId = employee.ScheduleId?.Value,
                ScheduleName = schedule?.Name,
                RestDay = (int?)employee.RestDay,
                RestDayName = employee.RestDay.HasValue ? GetDayName((int)employee.RestDay.Value) : null,
                OvertimeAuthorized = employee.OvertimeAuthorized,
                Gender = employee.Gender,
                OvertimeCalculationMethod = employee.OvertimeCalculationMethod,
                CardNumber = employee.CardNumber,
                DevicePassword = employee.DevicePassword,
                FingerprintCount = employee.Fingerprints?.Count ?? 0,
                HasFace = !string.IsNullOrEmpty(employee.FaceTemplate),
                OvertimeCapType = employee.OvertimeCapType,
                OvertimeCapMinutes = employee.OvertimeCapMinutes
            };

            return Result<EmployeeDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener empleado {EmployeeId}", request.Id);
            return Result<EmployeeDto>.Failure("Error al obtener el empleado");
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
