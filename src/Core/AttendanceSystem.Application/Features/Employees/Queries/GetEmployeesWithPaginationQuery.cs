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
            var employees = await _employeeRepository.GetAllAsync(cancellationToken);

            // Cargar datos relacionados para mostrar nombres (esto se hace en memoria por ahora)
            // Una optimización futura sería hacerlo via DB query con includes
            var branches = await _branchRepository.GetAllAsync(cancellationToken);
            var departments = await _departmentRepository.GetAllAsync(cancellationToken);
            var positions = await _positionRepository.GetAllAsync(cancellationToken);
            var shifts = await _shiftRepository.GetAllAsync(cancellationToken);

            var branchDict = branches.ToDictionary(b => b.Id, b => b.Name);
            var departmentDict = departments.ToDictionary(d => d.Id, d => d.Name);
            var positionDict = positions.ToDictionary(p => p.Id, p => p.Name);
            var shiftDict = shifts.ToDictionary(s => s.Id, s => s.Name);

            // Mapeo inicial a DTO
            var query = employees.Select(e => new EmployeeDto
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
            }).AsQueryable();

            // 1. Filtrado Global
            if (!string.IsNullOrWhiteSpace(request.SearchString))
            {
                query = query.Where(e =>
                    e.FullName.Contains(request.SearchString, StringComparison.OrdinalIgnoreCase) ||
                    e.Id.Contains(request.SearchString, StringComparison.OrdinalIgnoreCase) ||
                    e.BranchName.Contains(request.SearchString, StringComparison.OrdinalIgnoreCase) ||
                    e.DepartmentName.Contains(request.SearchString, StringComparison.OrdinalIgnoreCase) ||
                    e.PositionName.Contains(request.SearchString, StringComparison.OrdinalIgnoreCase));
            }

            // 2. Filtrado por Columna
            if (!string.IsNullOrWhiteSpace(request.IdFilter))
                query = query.Where(e => e.Id.Contains(request.IdFilter, StringComparison.OrdinalIgnoreCase));
            
            if (!string.IsNullOrWhiteSpace(request.NameFilter))
                query = query.Where(e => e.FullName.Contains(request.NameFilter, StringComparison.OrdinalIgnoreCase));

            if (request.BranchFilter != null && request.BranchFilter.Any())
            {
                // Filter by any of the selected branches
                query = query.Where(e => request.BranchFilter.Contains(e.BranchName));
            }

            if (request.DepartmentFilter != null && request.DepartmentFilter.Any())
            {
                query = query.Where(e => request.DepartmentFilter.Contains(e.DepartmentName));
            }

            if (request.PositionFilter != null && request.PositionFilter.Any())
            {
                query = query.Where(e => request.PositionFilter.Contains(e.PositionName));
            }

            if (request.StatusFilter != null && request.StatusFilter.Any())
            {
                // Status is enum, we compare string representation
                query = query.Where(e => request.StatusFilter.Contains(e.Status.ToString()));
            }

            // 2. Sorting
            // MudBlazor SortDirection: 0=None, 1=Ascending, 2=Descending
            if (!string.IsNullOrEmpty(request.SortLabel))
            {
                // Simple dynamic sorting based on property name
                // Note: Generics or Reflection is often used here, but specific switch is safer/faster
                bool ascending = request.SortDirection == 1;

                query = request.SortLabel switch
                {
                    "ID" => ascending ? query.OrderBy(e => e.Id.Length).ThenBy(e => e.Id) : query.OrderByDescending(e => e.Id.Length).ThenByDescending(e => e.Id),
                    "Nombre Completo" => ascending ? query.OrderBy(e => e.FullName) : query.OrderByDescending(e => e.FullName),
                    "Sucursal" => ascending ? query.OrderBy(e => e.BranchName) : query.OrderByDescending(e => e.BranchName),
                    "Departamento" => ascending ? query.OrderBy(e => e.DepartmentName) : query.OrderByDescending(e => e.DepartmentName),
                    "Puesto" => ascending ? query.OrderBy(e => e.PositionName) : query.OrderByDescending(e => e.PositionName),
                    "Estado" => ascending ? query.OrderBy(e => e.Status) : query.OrderByDescending(e => e.Status),
                    _ => query // Default no sort change
                };
            }
            else
            {
                // Default sort
                query = query.OrderBy(e => e.Id.Length).ThenBy(e => e.Id);
            }

            // 3. Paging
            var totalCount = query.Count();
            var items = query
                .Skip(request.PageNumber * request.PageSize) // MudTable uses 0-index pages? verify. Yes, assumption: PageNumber is 0-indexed.
                .Take(request.PageSize)
                .ToList();

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
