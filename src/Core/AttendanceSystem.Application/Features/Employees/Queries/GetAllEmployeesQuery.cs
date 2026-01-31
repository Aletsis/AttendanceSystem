using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Employees.Queries;

public sealed record GetAllEmployeesQuery : IRequest<Result<IReadOnlyList<EmployeeDto>>>;

public sealed class GetAllEmployeesQueryHandler : IRequestHandler<GetAllEmployeesQuery, Result<IReadOnlyList<EmployeeDto>>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IShiftRepository _shiftRepository;
    private readonly ILogger<GetAllEmployeesQueryHandler> _logger;

    public GetAllEmployeesQueryHandler(
        IEmployeeRepository employeeRepository,
        IBranchRepository branchRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository,
        IShiftRepository shiftRepository,
        ILogger<GetAllEmployeesQueryHandler> logger)
    {
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _shiftRepository = shiftRepository;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<EmployeeDto>>> Handle(GetAllEmployeesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var employees = await _employeeRepository.GetAllAsync(cancellationToken);

            // Cargar todas las entidades relacionadas
            var branches = await _branchRepository.GetAllAsync(cancellationToken);
            var departments = await _departmentRepository.GetAllAsync(cancellationToken);
            var positions = await _positionRepository.GetAllAsync(cancellationToken);
            var shifts = await _shiftRepository.GetAllAsync(cancellationToken);

            var branchDict = branches.ToDictionary(b => b.Id, b => b.Name);
            var departmentDict = departments.ToDictionary(d => d.Id, d => d.Name);
            var positionDict = positions.ToDictionary(p => p.Id, p => p.Name);
            var shiftDict = shifts.ToDictionary(s => s.Id, s => s.Name);

            var dtos = employees.Select(e => new EmployeeDto
            {
                Id = e.Id.Value,
                FirstName = e.FirstName,
                LastName = e.LastName,
                FullName = e.GetFullName(),
                Email = e.Email ?? string.Empty,
                PhoneNumber = e.PhoneNumber,
                HireDate = e.HireDate,
                Status = e.Status,
                BranchId = e.BranchId.Value,
                BranchName = branchDict.GetValueOrDefault(e.BranchId, "N/A"),
                DepartmentId = e.DepartmentId.Value,
                DepartmentName = departmentDict.GetValueOrDefault(e.DepartmentId, "N/A"),
                PositionId = e.PositionId.Value,
                PositionName = positionDict.GetValueOrDefault(e.PositionId, "N/A"),
                ShiftType = e.ShiftType,
                ScheduleId = e.ScheduleId?.Value,
                ScheduleName = e.ScheduleId != null ? shiftDict.GetValueOrDefault(e.ScheduleId, "N/A") : null,
                RestDay = (int?)e.RestDay,
                RestDayName = e.RestDay.HasValue ? GetDayName((int)e.RestDay.Value) : null,
                OvertimeAuthorized = e.OvertimeAuthorized,
                Gender = e.Gender,
                OvertimeCalculationMethod = e.OvertimeCalculationMethod,
                CardNumber = e.CardNumber,
                DevicePassword = e.DevicePassword,
                FingerprintCount = e.Fingerprints?.Count ?? 0,
                HasFace = !string.IsNullOrEmpty(e.FaceTemplate),
                OvertimeCapType = e.OvertimeCapType,
                OvertimeCapMinutes = e.OvertimeCapMinutes
            })
            .OrderBy(e => e.Id.Length).ThenBy(e => e.Id)
            .ToList();

            return Result<IReadOnlyList<EmployeeDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener empleados");
            return Result<IReadOnlyList<EmployeeDto>>.Failure("Error al obtener los empleados");
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
