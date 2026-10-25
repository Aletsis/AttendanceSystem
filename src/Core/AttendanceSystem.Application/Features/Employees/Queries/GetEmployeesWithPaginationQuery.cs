using AttendanceSystem.Application.Common;
using AttendanceSystem.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Employees.Queries;

public record PaginatedEmployeesDto(IReadOnlyList<EmployeeDto> Items, int TotalCount);

public sealed record GetEmployeesWithPaginationQuery(
    int PageNumber,
    int PageSize,
    string? SearchString,
    string? SortLabel,
    int? SortDirection, // 0 for None, 1 for Ascending, 2 for Descending (MudBlazor SortDirection enum mapping)
    string? IdFilter = null,
    string? NameFilter = null,
    IEnumerable<string>? BranchFilter = null,
    IEnumerable<string>? DepartmentFilter = null,
    IEnumerable<string>? PositionFilter = null,
    IEnumerable<string>? StatusFilter = null
) : IRequest<Result<PaginatedEmployeesDto>>;

public sealed class GetEmployeesWithPaginationQueryHandler : IRequestHandler<GetEmployeesWithPaginationQuery, Result<PaginatedEmployeesDto>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IShiftRepository _shiftRepository;
    private readonly ILogger<GetEmployeesWithPaginationQueryHandler> _logger;

    public GetEmployeesWithPaginationQueryHandler(
        IEmployeeRepository employeeRepository,
        IBranchRepository branchRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository,
        IShiftRepository shiftRepository,
        ILogger<GetEmployeesWithPaginationQueryHandler> logger)
    {
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _shiftRepository = shiftRepository;
        _logger = logger;
    }

    public async Task<Result<PaginatedEmployeesDto>> Handle(GetEmployeesWithPaginationQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var (employeesPage, totalCount) = await _employeeRepository.GetPaginatedAsync(
                request.PageNumber,
                request.PageSize,
                request.SearchString,
                request.SortLabel,
                request.SortDirection,
                request.IdFilter,
                request.NameFilter,
                request.BranchFilter,
                request.DepartmentFilter,
                request.PositionFilter,
                request.StatusFilter,
                cancellationToken);

            // Cargar diccionarios sólo para los elementos de la página actual (o catálogos livianos)
            var branches = await _branchRepository.GetAllAsync(cancellationToken);
            var departments = await _departmentRepository.GetAllAsync(cancellationToken);
            var positions = await _positionRepository.GetAllAsync(cancellationToken);
            var shifts = await _shiftRepository.GetAllAsync(cancellationToken);

            var branchDict = branches.ToDictionary(b => b.Id, b => b.Name);
            var departmentDict = departments.ToDictionary(d => d.Id, d => d.Name);
            var positionDict = positions.ToDictionary(p => p.Id, p => p.Name);
            var shiftDict = shifts.ToDictionary(s => s.Id, s => s.Name);

            // Mapeo selectivo únicamente para los elementos paginados
            var items = employeesPage.Select(e => new EmployeeDto
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
                RestDays = e.GetEffectiveRestDays().Select(d => (int)d).ToList(),
                RestDayName = e.GetEffectiveRestDays().Any() ? string.Join(", ", e.GetEffectiveRestDays().Select(d => GetDayName((int)d))) : null,
                OvertimeAuthorized = e.OvertimeAuthorized,
                Gender = e.Gender,
                OvertimeCalculationMethod = e.OvertimeCalculationMethod,
                CardNumber = e.CardNumber,
                DevicePassword = e.DevicePassword,
                FingerprintCount = e.Fingerprints?.Count ?? 0,
                HasFace = !string.IsNullOrEmpty(e.FaceTemplate),
                OvertimeCapType = e.OvertimeCapType,
                OvertimeCapMinutes = e.OvertimeCapMinutes,
                CalculateOvertimeBeforeEntry = e.CalculateOvertimeBeforeEntry,
                DevicePrivilege = e.DevicePrivilege,

                StatusPeriods = e.StatusPeriods.OrderByDescending(p => p.StartDate).Select(p => new StatusPeriodDto(p.Status, p.StartDate, p.EndDate)).ToList(),
                Photo = e.Photo,
                CreatedAt = e.CreatedAt,
                CreatedBy = e.CreatedBy,
                UpdatedAt = e.UpdatedAt,
                UpdatedBy = e.UpdatedBy
            }).ToList();

            return Result<PaginatedEmployeesDto>.Success(new PaginatedEmployeesDto(items, totalCount));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paginated employees");
            return Result<PaginatedEmployeesDto>.Failure("Error al obtener empleados paginados");
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
