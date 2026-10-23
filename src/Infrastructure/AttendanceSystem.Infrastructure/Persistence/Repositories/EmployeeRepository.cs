using AttendanceSystem.Domain.Aggregates.EmployeeAggregate;
using AttendanceSystem.Domain.Repositories;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class EmployeeRepository : IEmployeeRepository
{
    private readonly AttendanceDbContext _dbContext;

    public EmployeeRepository(AttendanceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Employee>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .Include(e => e.Fingerprints)
            .ToListAsync(cancellationToken);
    }

    public async Task<Employee?> GetByIdAsync(EmployeeId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .Include(e => e.Fingerprints)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Employee>> GetByBranchAsync(BranchId branchId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .Include(e => e.Fingerprints)
            .Where(e => e.BranchId == branchId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Employee>> GetByDepartmentAsync(DepartmentId departmentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .Include(e => e.Fingerprints)
            .Where(e => e.DepartmentId == departmentId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(EmployeeId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .AnyAsync(e => e.Id == id, cancellationToken);
    }

    public void Add(Employee employee)
    {
        _dbContext.Set<Employee>().Add(employee);
    }

    public void Update(Employee employee)
    {
        _dbContext.Set<Employee>().Update(employee);
    }

    public void Delete(Employee employee)
    {
        _dbContext.Set<Employee>().Remove(employee);
    }

    public async Task<bool> IsShiftInUseAsync(ShiftId shiftId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .AnyAsync(e => e.ScheduleId == shiftId || e.ScheduleAssignments.Any(a => a.ScheduleId == shiftId), cancellationToken);
    }

    public async Task<bool> IsDepartmentInUseAsync(DepartmentId departmentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .AnyAsync(e => e.DepartmentId == departmentId, cancellationToken);
    }

    public async Task<bool> IsPositionInUseAsync(PositionId positionId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Employee>()
            .AnyAsync(e => e.PositionId == positionId, cancellationToken);
    }

    private async Task<List<EmployeeId>> IdsContainingAsync(string text, CancellationToken cancellationToken)
    {
        var ids = await _dbContext.Set<Employee>().AsNoTracking().Select(e => e.Id).ToListAsync(cancellationToken);
        return ids.Where(id => id.Value.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPaginatedAsync(
        int pageNumber,
        int pageSize,
        string? searchString,
        string? sortLabel,
        int? sortDirection,
        string? idFilter = null,
        string? nameFilter = null,
        IEnumerable<string>? branchFilter = null,
        IEnumerable<string>? departmentFilter = null,
        IEnumerable<string>? positionFilter = null,
        IEnumerable<string>? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        // El Id es un objeto de valor convertido a texto: EF no traduce e.Id.Value a SQL (la consulta fallaba y la
        // lista de empleados quedaba vacía). Búsqueda: se resuelven primero los Id que coinciden (sólo la columna Id)
        // y se filtra con Contains; el orden natural por Id se calcula sobre la lista de Id. Funciona en SQL y en memoria.
        // Consulta base sin seguimiento (AsNoTracking) y sin cargar huellas/plantillas pesadas
        var query = _dbContext.Set<Employee>().AsNoTracking();

        // 1. Filtrado Global en base de datos
        if (!string.IsNullOrWhiteSpace(searchString))
        {
            var search = searchString.Trim();
            var matchingIds = await IdsContainingAsync(search, cancellationToken);
            query = query.Where(e =>
                e.FirstName.Contains(search) ||
                e.LastName.Contains(search) ||
                (e.FirstName + " " + e.LastName).Contains(search) ||
                matchingIds.Contains(e.Id));
        }

        // 2. Filtros por columna
        if (!string.IsNullOrWhiteSpace(idFilter))
        {
            var idMatches = await IdsContainingAsync(idFilter.Trim(), cancellationToken);
            query = query.Where(e => idMatches.Contains(e.Id));
        }

        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var nameF = nameFilter.Trim();
            query = query.Where(e =>
                e.FirstName.Contains(nameF) ||
                e.LastName.Contains(nameF) ||
                (e.FirstName + " " + e.LastName).Contains(nameF));
        }

        if (branchFilter != null && branchFilter.Any())
        {
            var branchList = branchFilter.ToList();
            var branchIds = await _dbContext.Branches
                .AsNoTracking()
                .Where(b => branchList.Contains(b.Name))
                .Select(b => b.Id)
                .ToListAsync(cancellationToken);

            query = query.Where(e => branchIds.Contains(e.BranchId));
        }

        if (departmentFilter != null && departmentFilter.Any())
        {
            var deptList = departmentFilter.ToList();
            var deptIds = await _dbContext.Departments
                .AsNoTracking()
                .Where(d => deptList.Contains(d.Name))
                .Select(d => d.Id)
                .ToListAsync(cancellationToken);

            query = query.Where(e => deptIds.Contains(e.DepartmentId));
        }

        if (positionFilter != null && positionFilter.Any())
        {
            var posList = positionFilter.ToList();
            var posIds = await _dbContext.Positions
                .AsNoTracking()
                .Where(p => posList.Contains(p.Name))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            query = query.Where(e => posIds.Contains(e.PositionId));
        }

        if (statusFilter != null && statusFilter.Any())
        {
            var statusEnums = new List<AttendanceSystem.Domain.Enumerations.EmployeeStatus>();
            foreach (var s in statusFilter)
            {
                if (Enum.TryParse<AttendanceSystem.Domain.Enumerations.EmployeeStatus>(s, true, out var parsedStatus))
                {
                    statusEnums.Add(parsedStatus);
                }
            }

            if (statusEnums.Any())
            {
                query = query.Where(e => statusEnums.Contains(e.Status));
            }
        }

        var safePageSize = pageSize > 0 ? pageSize : 20;
        var safePageNumber = Math.Max(0, pageNumber);
        bool ascending = sortDirection == 1;

        // 3. Orden por nombre o estado: en SQL.
        if (sortLabel is "Nombre Completo" or "Estado")
        {
            query = sortLabel == "Nombre Completo"
                ? (ascending ? query.OrderBy(e => e.FirstName).ThenBy(e => e.LastName) : query.OrderByDescending(e => e.FirstName).ThenByDescending(e => e.LastName))
                : (ascending ? query.OrderBy(e => e.Status) : query.OrderByDescending(e => e.Status));

            var total = await query.CountAsync(cancellationToken);
            var page = await query.Skip(safePageNumber * safePageSize).Take(safePageSize).ToListAsync(cancellationToken);
            return (page, total);
        }

        // 4. Orden por ID (el predeterminado), natural: longitud y luego valor ("2" antes que "10"). Se ordena la lista
        //    de Id ya filtrada en SQL (sólo esa columna) y se cargan únicamente los empleados de la página.
        var filteredIds = await query.Select(e => e.Id).ToListAsync(cancellationToken);
        var descending = !string.IsNullOrEmpty(sortLabel) && !ascending;
        var orderedIds = descending
            ? filteredIds.OrderByDescending(id => id.Value.Length).ThenByDescending(id => id.Value, StringComparer.Ordinal)
            : filteredIds.OrderBy(id => id.Value.Length).ThenBy(id => id.Value, StringComparer.Ordinal);
        var pageIds = orderedIds.Skip(safePageNumber * safePageSize).Take(safePageSize).ToList();

        var position = pageIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        var items = (await query.Where(e => pageIds.Contains(e.Id)).ToListAsync(cancellationToken))
            .OrderBy(e => position[e.Id])
            .ToList();

        return (items, filteredIds.Count);
    }
}
